using Assets.Scripts.Game.MapGeneration;
using HarmonyLib;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System;
using System.Collections.Generic;

namespace Megaphonk.Patches
{
    [HarmonyPatch(typeof(RandomObjectPlacer))]
    internal class RandomObjectPlacerPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// Skip object spawning on clients
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(RandomObjectPlacer.RandomObjectSpawner))]
        public static bool RandomObjectSpawner_Prefix(RandomObjectPlacer __instance)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
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

        /// <summary>
        /// Send spawned objects to clients
        /// </summary>

        [HarmonyPostfix]
        [HarmonyPatch(nameof(RandomObjectPlacer.RandomObjectSpawner))]
        public static void RandomObjectSpawner_Postfix(RandomObjectPlacer __instance, RandomMapObject randomObject)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && !isServer.Value)
            {
                return;
            }

            // One scene scan, every name read once (each read is a native call and a new string): scanning the whole scene
            // again for every prefab made map generation hitch
            var prefabNames = new List<string>();
            foreach (var prefab in randomObject.prefabs)
            {
                var prefabName = prefab.name;
                if (!prefabName.Contains("Microwave")) //Skip microwaves , handled in MicrowavePatches
                {
                    prefabNames.Add(prefabName);
                }
            }

            if (prefabNames.Count == 0)
            {
                return;
            }

            foreach (var obj in Il2CppFindHelper.FindAllGameObjects())
            {
                var name = obj.name;
                if (name == "BarrelMesh" || name == "ArchCollider")
                {
                    continue;
                }

                foreach (var prefabName in prefabNames)
                {
                    if (name.StartsWith(prefabName, StringComparison.Ordinal))
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
