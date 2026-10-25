using Assets.Scripts.Inventory__Items__Pickups;
using HarmonyLib;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Megaphonk.Patches
{
    [HarmonyPatch(typeof(PlayerHealth))]
    internal static class PlayerHealthPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();
        private static readonly IDownedService downedService = Plugin.Services.GetService<IDownedService>();

        public static bool Prepare(System.Reflection.MethodBase original)
        {
            return original == null || Helpers.NativeCodeGuard.IsSafeToPatch(original);
        }

        /// <summary>
        /// Get downed instead of dying when a teammate can still revive
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerHealth.PlayerDied))]
        public static bool PlayerDied_Prefix(PlayerHealth __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (!IsLocalHealth(__instance)) //A copy's death would end the local run
            {
                return false;
            }

            return !downedService.TryGoDown(__instance);
        }

        /// <summary>
        /// Downed players are invulnerable: bleeding out is the threat.
        /// Copies of remote players never take damage here: their hp comes from the network, and a lethal hit on a copy
        /// used the local player's Za Warudo or killed the local run.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerHealth.Damage))]
        public static bool Damage_Prefix(PlayerHealth __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            return IsLocalHealth(__instance) && !downedService.IsLocalDowned;
        }

        /// <summary>
        /// Synchronize player death event
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PlayerHealth.PlayerDied))]
        public static void PlayerDied_Postfix(PlayerHealth __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (downedService.IsLocalDowned || !IsLocalHealth(__instance)) //Got downed instead, or a copy (the prefix skipped the death)
            {
                return;
            }

            synchronizationService.OnPlayerDied();
        }

        /// <summary>
        /// Prevent remote inventory tick
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerHealth.Tick))]
        public static bool Tick_Prefix(PlayerHealth __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isRemotePlayer = playerManagerService.IsRemotePlayerHealth(__instance);
            if (isRemotePlayer)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// True for the local player's health (and when it can't be found: the game's own behaviour)
        /// </summary>
        private static bool IsLocalHealth(PlayerHealth instance)
        {
            var localHealth = GameManager.Instance?.player?.inventory?.playerHealth;
            return localHealth == null || instance.Pointer == localHealth.Pointer;
        }
    }
}
