using HarmonyLib;
using Megaphonk.Configuration;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Megaphonk.Patches
{
    /// <summary>
    /// Prevent saving during netplay sessions
    /// </summary>
    [HarmonyPatch(typeof(SaveManager))]
    internal static class SaveManagerPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetRequiredService<ISynchronizationService>();

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveStats))]
        public static bool SaveGame_Prefix()
        {
            if (synchronizationService.HasNetplaySessionInitialized() || synchronizationService.IsLoadingNextLevel())
            {
                if (!ModConfig.AllowSavesDuringNetplay.Value)
                {
                    Plugin.Log.LogInfo("Skipping SaveStats during netplay session");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveProgression))]
        public static bool SaveProgression_Prefix()
        {
            if (synchronizationService.HasNetplaySessionInitialized() || synchronizationService.IsLoadingNextLevel())
            {
                if (!ModConfig.AllowSavesDuringNetplay.Value)
                {
                    Plugin.Log.LogInfo("Skipping SaveProgression during netplay session");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveConfig))]
        public static bool SaveConfig_Prefix()
        {
            if (synchronizationService.HasNetplaySessionInitialized() || synchronizationService.IsLoadingNextLevel())
            {
                if (!ModConfig.AllowSavesDuringNetplay.Value)
                {
                    Plugin.Log.LogInfo("Skipping SaveConfig during netplay session");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveTemp))]
        public static bool SaveTemp_Prefix()
        {
            if (synchronizationService.HasNetplaySessionInitialized() || synchronizationService.IsLoadingNextLevel())
            {
                if (!ModConfig.AllowSavesDuringNetplay.Value)
                {
                    Plugin.Log.LogInfo("Skipping SaveTemp during netplay session");
                    return false;
                }
            }
            return true;
        }
    }
}
