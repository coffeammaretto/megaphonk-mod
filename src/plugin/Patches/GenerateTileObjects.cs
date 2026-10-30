using Assets.Scripts.Managers;
using HarmonyLib;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System;
using System.Collections.Generic;

namespace Megaphonk.Patches
{
    /// <summary> 
    /// Used on Tiled Map 
    /// </summary>
    [HarmonyPatch(typeof(GenerateTileObjects))]
    internal class GenerateTileObjectsPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// Add boss spawner prefabs and also specific prefab like coffin in graveyard (Spawned by server message)
        /// </summary>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GenerateTileObjects.Generate))]
        public static bool Generate_Prefix(GenerateTileObjects __instance, StageData stageData)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;

            if (!isServer)
            {
                foreach (var prefab in stageData.stageTilePrefabs.flatTilePrefabs)
                {
                    Plugin.Instance.AddPrefab(prefab);
                }

                foreach (var prefab in stageData.stageTilePrefabs.mapSpecificTilesPrefabs)
                {
                    Plugin.Instance.AddPrefab(prefab);
                }

                Plugin.Instance.AddPrefab(__instance.bossSpawner);
                Plugin.Instance.AddPrefab(__instance.bossSpawnerFinal);

                if (MapController.currentMap.eMap == Assets.Scripts._Data.MapsAndStages.EMap.Graveyard)
                {
                    Plugin.Instance.AddPrefab(__instance.graveyardBossPortal);
                }

                Plugin.Log.LogInfo("Skipping tile object spawning on client");

                return false;
            }

            return true;
        }

        /// <summary>
        /// Send Boss spawner , specific if any and graveyard portal if any info to clients
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(GenerateTileObjects.Generate))]
        public static void Generate_Postfix(GenerateTileObjects __instance, StageData stageData)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;

            if (!isServer)
            {
                return;
            }

            // Every name read once (each read is a native call and a new string), one pass over the scene
            var prefixes = new List<string>();
            foreach (var prefab in stageData.stageTilePrefabs.flatTilePrefabs)
            {
                prefixes.Add(prefab.name);
            }

            foreach (var prefab in stageData.stageTilePrefabs.mapSpecificTilesPrefabs)
            {
                prefixes.Add(prefab.name);
            }

            prefixes.Add(__instance.bossSpawner.name);
            prefixes.Add(__instance.bossSpawnerFinal.name);
            if (MapController.currentMap.eMap == Assets.Scripts._Data.MapsAndStages.EMap.Graveyard)
            {
                prefixes.Add(__instance.graveyardBossPortal.name);
            }

            foreach (var obj in Il2CppFindHelper.FindAllGameObjects())
            {
                var name = obj.name;
                foreach (var prefix in prefixes)
                {
                    if (name.StartsWith(prefix, StringComparison.Ordinal))
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
