using Assets.Scripts.Utility;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(PauseUi))]
    internal static class PauseUiPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IUdpClientService udpClientService = Plugin.Services.GetService<IUdpClientService>();

        /// <summary>
        /// Prevent pause (shared experience or not) on pausing screen, unless alone in the room
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PauseUi.OnEnable))]
        public static void OnEnable_Postfix(PauseUi __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted() || udpClientService.IsSoloSession())
            {
                return;
            }

            MyTime.Unpause();
        }

    }
}
