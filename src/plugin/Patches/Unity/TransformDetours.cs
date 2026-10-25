using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Megaphonk.Patches.Unity
{
    /// <summary>
    /// Native hooks on Component.transform, Transform.position and Transform.rotation. The game calls them hundreds of
    /// thousands of times a second in the late game (pickups flying to the player, enemies): as Harmony patches each call
    /// went through the interop trampoline, which wraps the object and the result (700 MB of garbage every 10 s, a good part
    /// of the frame). Here the common case is a pointer check and the original call, without any allocation. Wrappers are
    /// built and the UnityComponentPatches/TransformPatches prefixes run only when a net player position request is pending
    /// or the object is destroyed.
    /// Never lets an exception or a call on a destroyed object reach the original: both would unwind native code through
    /// this hook and crash the game.
    /// </summary>
    internal static class TransformDetours
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Vec3
        {
            public float X, Y, Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Quat
        {
            public float X, Y, Z, W;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetTransformFn(IntPtr self, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate Vec3 GetPositionFn(IntPtr self, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate Quat GetRotationFn(IntPtr self, IntPtr methodInfo);

        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        // Hooks, originals and detours kept referenced for the whole session: native code calls through them
        private static GetTransformFn getTransformHook, getTransformOriginal;
        private static GetPositionFn getPositionHook, getPositionOriginal;
        private static GetRotationFn getRotationHook, getRotationOriginal;
        private static INativeDetour getTransformDetour, getPositionDetour, getRotationDetour;

        private static bool errorLogged;

        /// <summary>
        /// Hooks the three getters natively. False (nothing hooked) if one of them can't be: ApplyHarmonyFallback is then used
        /// </summary>
        public static bool TryApply()
        {
            try
            {
                if (!Il2CppObjectExtensions.TryGetCachedPtr(IntPtr.Zero, out _))
                {
                    Plugin.Log.LogWarning("Transform hooks: m_CachedPtr unavailable, destroyed objects can't be detected natively");
                    return false;
                }

                var getTransform = AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));
                var getPosition = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
                var getRotation = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.rotation));

                if (!TryGetNativeCode(getTransform, out var transformCode)
                    || !TryGetNativeCode(getPosition, out var positionCode)
                    || !TryGetNativeCode(getRotation, out var rotationCode))
                {
                    return false;
                }

                getTransformHook = GetTransformHook;
                getPositionHook = GetPositionHook;
                getRotationHook = GetRotationHook;
                getTransformDetour = INativeDetour.CreateAndApply(transformCode, getTransformHook, out getTransformOriginal);
                getPositionDetour = INativeDetour.CreateAndApply(positionCode, getPositionHook, out getPositionOriginal);
                getRotationDetour = INativeDetour.CreateAndApply(rotationCode, getRotationHook, out getRotationOriginal);

                Plugin.Log.LogInfo("Transform hooks: native (Component.transform, Transform.position, Transform.rotation)");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Transform hooks: native hooking failed, using Harmony patches: {ex}");
                getTransformDetour?.Dispose();
                getPositionDetour?.Dispose();
                getRotationDetour?.Dispose();
                getTransformDetour = getPositionDetour = getRotationDetour = null;
                return false;
            }
        }

        /// <summary>
        /// The previous Harmony patches (slower, same behaviour)
        /// </summary>
        public static void ApplyHarmonyFallback(Harmony harmony)
        {
            Patch(harmony, AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform)),
                typeof(UnityComponentPatches), nameof(UnityComponentPatches.get_transform_Prefix));
            Patch(harmony, AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position)),
                typeof(TransformPatches), nameof(TransformPatches.get_position_Prefix));
            Patch(harmony, AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.rotation)),
                typeof(TransformPatches), nameof(TransformPatches.get_rotation_Prefix));
        }

        private static void Patch(Harmony harmony, MethodInfo original, Type patchType, string prefix)
        {
            try
            {
                harmony.Patch(original, prefix: new HarmonyMethod(AccessTools.Method(patchType, prefix)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Harmony patching failed for {original?.DeclaringType?.Name}.{original?.Name}: {ex}");
            }
        }

        private static bool TryGetNativeCode(MethodInfo method, out IntPtr code)
        {
            code = IntPtr.Zero;
            if (method == null || !NativeCodeGuard.IsSafeToPatch(method))
            {
                Plugin.Log.LogWarning($"Transform hooks: can't hook {method?.DeclaringType?.Name}.{method?.Name}");
                return false;
            }

            var methodInfoField = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
            var methodInfo = methodInfoField != null ? (IntPtr)methodInfoField.GetValue(null) : IntPtr.Zero;
            code = methodInfo != IntPtr.Zero ? Marshal.ReadIntPtr(methodInfo) : IntPtr.Zero; // Il2CppMethodInfo.methodPointer
            return code != IntPtr.Zero;
        }

        /// <summary>
        /// Same as Unity's != null for engine objects: a zero pointer or a destroyed native object
        /// </summary>
        private static bool IsAlive(IntPtr self)
        {
            return self != IntPtr.Zero && Il2CppObjectExtensions.TryGetCachedPtr(self, out var cachedPtr) && cachedPtr != IntPtr.Zero;
        }

        private static bool HasPendingRequest()
        {
            return synchronizationService.HasNetplaySessionStarted() && playerManagerService.PeakNetplayerPositionRequest().HasValue;
        }

        private static void LogErrorOnce(Exception ex)
        {
            if (!errorLogged)
            {
                errorLogged = true;
                Plugin.Log.LogError($"Transform hooks: {ex}");
            }
        }

        private static IntPtr GetTransformHook(IntPtr self, IntPtr methodInfo)
        {
            var alive = IsAlive(self);
            if (alive && !HasPendingRequest())
            {
                return getTransformOriginal(self, methodInfo);
            }

            try
            {
                Transform result = null;
                var instance = self != IntPtr.Zero ? IL2CPP.PointerToValueGeneric<Component>(self, false, false) : null;
                if (!UnityComponentPatches.get_transform_Prefix(instance, ref result))
                {
                    return result is null ? IntPtr.Zero : result.Ptr();
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce(ex);
            }

            return alive ? getTransformOriginal(self, methodInfo) : IntPtr.Zero;
        }

        private static Vec3 GetPositionHook(IntPtr self, IntPtr methodInfo)
        {
            var alive = IsAlive(self);
            if (alive && !HasPendingRequest())
            {
                return getPositionOriginal(self, methodInfo);
            }

            try
            {
                var result = Vector3.zero;
                var instance = self != IntPtr.Zero ? IL2CPP.PointerToValueGeneric<Transform>(self, false, false) : null;
                if (!TransformPatches.get_position_Prefix(instance, ref result))
                {
                    return new Vec3 { X = result.x, Y = result.y, Z = result.z };
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce(ex);
            }

            return alive ? getPositionOriginal(self, methodInfo) : default;
        }

        private static Quat GetRotationHook(IntPtr self, IntPtr methodInfo)
        {
            var alive = IsAlive(self);
            if (alive && !HasPendingRequest())
            {
                return getRotationOriginal(self, methodInfo);
            }

            try
            {
                var result = Quaternion.identity;
                var instance = self != IntPtr.Zero ? IL2CPP.PointerToValueGeneric<Transform>(self, false, false) : null;
                if (!TransformPatches.get_rotation_Prefix(instance, ref result))
                {
                    return new Quat { X = result.x, Y = result.y, Z = result.z, W = result.w };
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce(ex);
            }

            return alive ? getRotationOriginal(self, methodInfo) : new Quat { W = 1f };
        }
    }
}
