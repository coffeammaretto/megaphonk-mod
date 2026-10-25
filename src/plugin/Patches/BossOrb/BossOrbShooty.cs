using HarmonyLib;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Megaphonk.Patches.BossOrbPatch
{
    [HarmonyPatch(typeof(BossOrbShooty))]
    internal static class BossOrbShootyPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// Skip on client to prevent trail artifacts.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(BossOrbShooty.FloatMovement))]
        public static bool FloatMovement_Prefix(BossOrbShooty __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }
            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return false;
            }
            return true;
        }
    }
}
