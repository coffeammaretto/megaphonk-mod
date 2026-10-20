using HarmonyLib;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegabonkTogether.Patches.Unity
{
    [HarmonyPatch(typeof(Component))]
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
        [HarmonyPrefix]
        [HarmonyPatch("get_transform")]
        public static bool get_transform_Prefix(Component __instance, ref Transform __result)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (IsNullOrDestroyed(__instance)) //TODO: i'm pretty sure its a netplayer dangling reference but how do i even debug this...
            {
                __result = GameManager.Instance.player.transform; //Hack ¯\_(ツ)_/¯
                return false;
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

    [HarmonyPatch(typeof(Transform))]
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
        [HarmonyPrefix]
        [HarmonyPatch("get_position")]
        public static bool get_position_Prefix(Transform __instance, ref Vector3 __result)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (UnityComponentPatches.IsNullOrDestroyed(__instance))
            {
                __result = GameManager.Instance.player.transform.position;
                return false;
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
        [HarmonyPrefix]
        [HarmonyPatch("get_rotation")]
        public static bool get_rotation_Prefix(Transform __instance, ref Quaternion __result)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (UnityComponentPatches.IsNullOrDestroyed(__instance))
            {
                __result = GameManager.Instance.player.transform.rotation;
                return false;
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
