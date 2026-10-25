using Assets.Scripts.Inventory__Items__Pickups.Interactables;
using Assets.Scripts.Managers;
using HarmonyLib;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace Megaphonk.Patches
{
    /// <summary>
    /// Co-op endgame (AscensionService): a copy of the final portal next to it starts the next ascension, the final portal
    /// itself still ends the run (character unlocks, map progress)
    /// </summary>
    [HarmonyPatch]
    internal static class AscensionPatches
    {
        private const string PORTAL_NAME = "Megaphonk_AscensionPortal";
        private const float PORTAL_SPACING = 10f;

        private static readonly IAscensionService ascensionService = Plugin.Services.GetService<IAscensionService>();

        public static bool IsAscensionPortal(Component interactable)
        {
            return interactable != null && interactable.gameObject.name.StartsWith(PORTAL_NAME);
        }

        /// <summary>
        /// The final portal shows up (the boss is dead): its ascension twin appears next to it
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseInteractable), nameof(BaseInteractable.Start))]
        public static void Start_Postfix(BaseInteractable __instance)
        {
            var finalPortal = __instance.GetComponent<InteractablePortalFinal>();
            if (finalPortal == null || IsAscensionPortal(finalPortal) || !ascensionService.CanPlacePortal)
            {
                return;
            }

            var original = finalPortal.transform;
            var twin = Object.Instantiate(finalPortal.gameObject, original.position + original.right * PORTAL_SPACING, original.rotation, original.parent);
            twin.name = PORTAL_NAME;
            Plugin.Log.LogInfo("Ascension portal placed next to the final portal");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InteractablePortalFinal), nameof(InteractablePortalFinal.Interact))]
        public static bool FinalPortalInteract_Prefix(InteractablePortalFinal __instance, ref bool __result)
        {
            if (!IsAscensionPortal(__instance))
            {
                return true;
            }

            ascensionService.UsePortal();
            __result = true;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InteractablePortalFinal), nameof(InteractablePortalFinal.GetInteractString))]
        public static void FinalPortalGetInteractString_Postfix(InteractablePortalFinal __instance, ref string __result)
        {
            if (IsAscensionPortal(__instance))
            {
                __result = Loc.F("Ascend (level {0})", ascensionService.Level + 1);
            }
        }

        /// <summary>
        /// A new run (or a restart) starts at no ascension
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapController), nameof(MapController.StartNewMap))]
        public static void StartNewMap_Postfix(bool __runOriginal)
        {
            if (__runOriginal) //Not cancelled by the netplay patches (a restart is blocked during a session)
            {
                ascensionService.Reset();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapController), nameof(MapController.RestartRun))]
        public static void RestartRun_Postfix(bool __runOriginal)
        {
            if (__runOriginal)
            {
                ascensionService.Reset();
            }
        }
    }
}
