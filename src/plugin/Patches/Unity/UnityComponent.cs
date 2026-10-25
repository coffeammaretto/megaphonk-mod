using HarmonyLib;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Megaphonk.Patches.Unity
{
    /// <summary>
    /// Not attribute patched: these getters are called hundreds of thousands of times a second in the late game. They are
    /// hooked natively by TransformDetours, which only builds wrappers and calls these prefixes when a net player position
    /// request is pending or the object is destroyed. The Harmony prefixes are the fallback if the native hooks can't be set.
    /// </summary>
    internal static class UnityComponentPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// Whether an object is named "Player", per (pointer, native pointer): reading the name is a native call plus a string
        /// copy, on every transform read while a position request is pending
        /// </summary>
        private static readonly Dictionary<(IntPtr, IntPtr), bool> isNamedPlayer = [];

        public static void ResetCaches()
        {
            isNamedPlayer.Clear();
            TransformPatches.ResetCaches();
        }

        /// <summary>
        /// Same result as the <c>__instance == null</c> native comparison for live objects, without the native call:
        /// only a missing or destroyed object goes through the real comparison
        /// </summary>
        internal static bool IsNullOrDestroyed(UnityEngine.Object instance)
        {
            return (instance is null || instance.CachedPtr() == IntPtr.Zero) && instance == null;
        }

        /// <summary>
        /// Name check cached per object. Bounded: cleared when it grows too large (and on level change)
        /// </summary>
        internal static bool HasName(Dictionary<(IntPtr, IntPtr), bool> cache, UnityEngine.Object instance, string name)
        {
            var key = (instance.Ptr(), instance.CachedPtr());
            if (!cache.TryGetValue(key, out var matches))
            {
                if (cache.Count >= 4096)
                {
                    cache.Clear();
                }

                matches = instance.name == name;
                cache[key] = matches;
            }

            return matches;
        }

        /// <summary>
        /// Intercept Component.transform getter to return the correct transform
        /// Needed for DragonBreath or for special attacks that target other players (Special attack always target the local player, if only they used the enemy.target rigidbody instead ¯\_(ツ)_/¯)
        /// </summary>
        public static bool get_transform_Prefix(Component __instance, ref Transform __result)
        {
            if (IsNullOrDestroyed(__instance)) //TODO: i'm pretty sure its a netplayer dangling reference but how do i even debug this...
            {
                // Hack ¯\_(ツ)_/¯. Never call the original on it: it throws, and out of netplay (left over objects after a
                // session) that flooded the log with errors
                __result = synchronizationService.HasNetplaySessionStarted() ? GameManager.Instance?.player?.transform : null;
                return false;
            }

            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var pendingRequest = playerManagerService.PeakNetplayerPositionRequest();
            if (pendingRequest.HasValue && HasName(isNamedPlayer, __instance, "Player"))
            {
                var netPlayerId = pendingRequest.Value;
                var localPlayerId = playerManagerService.GetLocalPlayer().ConnectionId;
                if (netPlayerId == localPlayerId)
                {
                    return true;
                }

                var netPlayer = playerManagerService.GetNetPlayerByNetplayId(netPlayerId);
                if (netPlayer == null)
                {
                    Plugin.Log.LogWarning($"get_transform_Prefix: NetPlayer with NetplayId {netPlayerId} not found");
                    return true;
                }
                __result = netPlayer.Model.transform;

                return false;
            }


            return true;
        }
    }

    internal static class TransformPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        private static readonly Dictionary<(IntPtr, IntPtr), bool> isNamedHips = [];
        private static readonly Dictionary<(IntPtr, IntPtr), bool> isNamedRenderer = [];

        public static void ResetCaches()
        {
            isNamedHips.Clear();
            isNamedRenderer.Clear();
        }

        /// <summary>
        /// Intercept Component.transform getter to return the correct transform (Work like above but for Transform component)
        /// Used by LaserBeamGun
        /// </summary>
        public static bool get_position_Prefix(Transform __instance, ref Vector3 __result)
        {
            if (UnityComponentPatches.IsNullOrDestroyed(__instance))
            {
                var player = synchronizationService.HasNetplaySessionStarted() ? GameManager.Instance?.player : null;
                __result = player != null ? player.transform.position : Vector3.zero;
                return false;
            }

            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }


            var pendingRequest = playerManagerService.PeakNetplayerPositionRequest();
            if (pendingRequest.HasValue && UnityComponentPatches.HasName(isNamedHips, __instance, "Hips"))
            {
                var netPlayerId = pendingRequest.Value;
                var localPlayerId = playerManagerService.GetLocalPlayer().ConnectionId;
                if (netPlayerId == localPlayerId)
                {
                    return true;
                }

                var netPlayer = playerManagerService.GetNetPlayerByNetplayId(netPlayerId);
                if (netPlayer == null)
                {
                    return true;
                }
                __result = netPlayer.Model.transform.position;
                __result.y += Plugin.PLAYER_FEET_OFFSET_Y;

                return false;
            }


            return true;
        }

        /// <summary>
        /// Intercept Component.transform getter to return the correct transform (Work like above)
        /// Used by ProjectileMelee (Sword)
        /// </summary>
        public static bool get_rotation_Prefix(Transform __instance, ref Quaternion __result)
        {
            if (UnityComponentPatches.IsNullOrDestroyed(__instance))
            {
                var player = synchronizationService.HasNetplaySessionStarted() ? GameManager.Instance?.player : null;
                __result = player != null ? player.transform.rotation : Quaternion.identity;
                return false;
            }

            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var pendingRequest = playerManagerService.PeakNetplayerPositionRequest();
            if (pendingRequest.HasValue && UnityComponentPatches.HasName(isNamedRenderer, __instance, "Renderer"))
            {
                var netPlayerId = pendingRequest.Value;
                var localPlayerId = playerManagerService.GetLocalPlayer().ConnectionId;

                if (netPlayerId == localPlayerId)
                {
                    return true;
                }

                var netPlayer = playerManagerService.GetNetPlayerByNetplayId(netPlayerId);
                if (netPlayer == null)
                {
                    return true;
                }

                __result = netPlayer.Model.transform.rotation;
                return false;
            }

            return true;
        }
    }
}
