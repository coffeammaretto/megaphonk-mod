using Assets.Scripts.Inventory__Items__Pickups.Interactables;
using Assets.Scripts.Managers;
using HarmonyLib;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
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
        private const float PLACE_DELAY = 2f; //The game finishes setting the final portal up (visuals, rising) first
        private const float FOLLOW_SECONDS = 10f;

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

            CoroutineRunner.Instance.Run(PlaceTwin(finalPortal.gameObject));
        }

        /// <summary>
        /// A copy of the final portal once the game has set it up: a copy made in its Start kept the state from before
        /// (it stayed invisible). It then follows the final portal for a while, in case it still moves (rising).
        /// </summary>
        private static IEnumerator PlaceTwin(GameObject original)
        {
            var placeAt = Time.realtimeSinceStartup + PLACE_DELAY;
            while (Time.realtimeSinceStartup < placeAt)
            {
                yield return null;
            }

            if (original == null)
            {
                yield break;
            }

            var source = original.transform;
            var offset = source.right * PORTAL_SPACING;
            var twin = Object.Instantiate(original, source.position + offset, source.rotation, source.parent);
            twin.name = PORTAL_NAME;
            twin.SetActive(true);
            Plugin.Log.LogInfo($"Ascension portal placed next to the final portal (final portal active: {original.activeInHierarchy}, at {source.position})");

            var followUntil = Time.realtimeSinceStartup + FOLLOW_SECONDS;
            while (Time.realtimeSinceStartup < followUntil && original != null && twin != null)
            {
                twin.transform.SetPositionAndRotation(original.transform.position + offset, original.transform.rotation);
                CopyActiveStates(original.transform, twin.transform);
                yield return null;
            }
        }

        /// <summary>
        /// Same children shown as on the final portal (the game may turn its visuals on after the copy)
        /// </summary>
        private static void CopyActiveStates(Transform source, Transform copy)
        {
            var count = Mathf.Min(source.childCount, copy.childCount);
            for (var i = 0; i < count; i++)
            {
                var sourceChild = source.GetChild(i);
                var copyChild = copy.GetChild(i);
                if (copyChild.gameObject.activeSelf != sourceChild.gameObject.activeSelf)
                {
                    copyChild.gameObject.SetActive(sourceChild.gameObject.activeSelf);
                }
                CopyActiveStates(sourceChild, copyChild);
            }
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
                Plugin.Services.GetService<IRiftService>()?.Reset();
                Plugin.Services.GetService<IPerkService>()?.Reset();
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
