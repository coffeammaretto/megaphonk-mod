using Assets.Scripts.Managers;
using HarmonyLib;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Megaphonk.Patches
{
    [HarmonyPatch(typeof(SpawnInteractables))]
    internal class SpawnInteractablesPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IGameBalanceService gameBalanceService = Plugin.Services.GetService<IGameBalanceService>();

        /// <summary>
        /// Add prefabs (Spawned by server)
        /// Also adjust free chest spawn rate
        /// </summary>  
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnChests))]
        public static bool SpawnChests_Prefix(SpawnInteractables __instance)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode();
            if (!isServer.HasValue || !isServer.Value)
            {
                Plugin.Log.LogInfo("Skipping chest spawning on client");
                Plugin.Instance.AddPrefab(__instance.chest);
                Plugin.Instance.AddPrefab(__instance.chestFree);
                return false;
            }

            var freeChestMultiplier = gameBalanceService.GetFreeChestSpawnRateMultiplier();

            __instance.chanceForFreeChest *= freeChestMultiplier;

            return true;
        }

        /// <summary>
        /// Send spawned chests to clients
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnChests))]
        public static void SpawnChests_Postfix(SpawnInteractables __instance)
        {
            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && isServer.Value)
            {
                // Names read once (each read is a native call and a new string). Chests and free chests, not the crypt ones
                var chestName = __instance.chest.name;
                var chestFreeName = __instance.chestFree.name;
                foreach (var obj in Il2CppFindHelper.FindAllGameObjects())
                {
                    var name = obj.name;
                    if ((name.StartsWith(chestName, StringComparison.Ordinal) || name.StartsWith(chestFreeName, StringComparison.Ordinal))
                        && !name.StartsWith("ChestFreeCrypt", StringComparison.Ordinal))
                    {
                        var dynamic = DynamicData.For(obj);
                        var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                        if (!hasBeenSet.HasValue)
                        {
                            synchronizationService.OnSpawnedObject(obj);
                            dynamic.Set("hasBeenSetByServer", true);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Add prefabs (Spawned by server)
        /// </summary>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnRails))]
        public static bool SpawnRails_Prefix(SpawnInteractables __instance)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode();
            if (!isServer.HasValue || !isServer.Value)
            {
                Plugin.Log.LogInfo("Skipping rail spawning on client");
                Plugin.Instance.AddPrefab(__instance.chest);
                Plugin.Instance.AddPrefab(__instance.chestFree);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Send spawned rails to clients
        /// </summary>

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnRails))]
        public static void SpawnRails_Postfix(SpawnInteractables __instance)
        {
            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && isServer.Value)
            {
                var railNames = new List<string>();
                foreach (var rail in __instance.rails)
                {
                    railNames.Add(rail.name);
                }

                foreach (var obj in Il2CppFindHelper.FindAllGameObjects())
                {
                    var name = obj.name;
                    foreach (var railName in railNames)
                    {
                        if (name.StartsWith(railName, StringComparison.Ordinal))
                        {
                            var dynamic = DynamicData.For(obj);
                            var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                            if (!hasBeenSet.HasValue)
                            {
                                synchronizationService.OnSpawnedObject(obj);
                                dynamic.Set("hasBeenSetByServer", true);
                            }
                            break;
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Prevent shrine spawning on clients (Spawned by server) (Do you know a shadyGuy is a shrine ?)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnShrines))]
        public static bool SpawnShrines_Prefix(SpawnInteractables __instance)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;

            if (!isServer)
            {
                //For clients, shrines prefabs already added in MapGenerationController
                return false;
            }
            return true;
        }

        /// <summary>
        /// Send spawned shrines to clients. Intentionnaly omit shadyGuy and microwave, we want to wait for the rarity to be set before sending those (Will be sended at ShadyGuy.Start patch and Microwave.Start patch)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnShrines))]
        public static void SpawnShrines_Postfix(SpawnInteractables __instance)
        {
            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && isServer.Value)
            {
                // Filtered once: the lazy filter used to run again (two GetComponentInChildren per shrine) for every object of
                // the scene
                var shrineNames = new List<string>();
                foreach (var shrine in MapController.currentMap.shrines)
                {
                    if (shrine.GetComponentInChildren<InteractableShadyGuy>() == null && shrine.GetComponentInChildren<InteractableMicrowave>() == null) //Handle in those respective patches
                    {
                        shrineNames.Add(shrine.name);
                    }
                }

                foreach (var obj in Il2CppFindHelper.FindAllGameObjects())
                {
                    var name = obj.name;
                    foreach (var shrineName in shrineNames)
                    {
                        if (name.StartsWith(shrineName, StringComparison.Ordinal))
                        {
                            var dynamic = DynamicData.For(obj);
                            var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                            if (!hasBeenSet.HasValue)
                            {
                                synchronizationService.OnSpawnedObject(obj);
                                dynamic.Set("hasBeenSetByServer", true);
                            }
                            break;
                        }
                    }
                }
            }
        }
    }
}
