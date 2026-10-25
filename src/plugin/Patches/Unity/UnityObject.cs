using Assets.Scripts.Managers;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Megaphonk.Patches.Unity
{
    [HarmonyPatch(typeof(UnityEngine.Object))]
    internal static class UnityObjectPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IFinalBossOrbManagerService finalOrbManagerService = Plugin.Services.GetService<IFinalBossOrbManagerService>();
        private static readonly IChestManagerService chestManagerService = Plugin.Services.GetService<IChestManagerService>();

        // Quest and desert grave prefabs by pointer, built once per EffectManager (level) instead of 11 native == per Instantiate
        private static (IntPtr Pointer, IntPtr CachedPtr) prefabSetsOwner;
        private static readonly HashSet<IntPtr> questPrefabs = [];
        private static readonly HashSet<IntPtr> desertGravePrefabs = [];

        // original.name of the Instantiate call in progress: read once, shared by the prefixes and the postfix (which clears it)
        private static IntPtr currentOriginalPointer;
        private static string currentOriginalName;

        public static void ResetCaches()
        {
            prefabSetsOwner = default;
            questPrefabs.Clear();
            desertGravePrefabs.Clear();
            currentOriginalPointer = IntPtr.Zero;
            currentOriginalName = null;
        }

        private static bool IsAlive(UnityEngine.Object obj)
        {
            return obj is not null && obj.CachedPtr() != IntPtr.Zero;
        }

        /// <summary>
        /// Same as Unity's == for a non null original, without the native call
        /// </summary>
        private static bool IsSameObject(UnityEngine.Object original, UnityEngine.Object other)
        {
            return other is not null && original.Ptr() == other.Ptr();
        }

        private static EffectManager GetEffectManager()
        {
            var effectManager = EffectManager.Instance;
            return IsAlive(effectManager) ? effectManager : null;
        }

        private static string GetOriginalName(UnityEngine.Object original)
        {
            var pointer = original.Ptr();
            if (currentOriginalName == null || currentOriginalPointer != pointer)
            {
                currentOriginalName = original.name;
                currentOriginalPointer = pointer;
            }

            return currentOriginalName;
        }

        private static void EnsurePrefabSets(EffectManager effectManager)
        {
            var owner = (effectManager.Ptr(), effectManager.CachedPtr());
            if (owner == prefabSetsOwner)
            {
                return;
            }

            questPrefabs.Clear();
            desertGravePrefabs.Clear();

            foreach (var quest in new UnityEngine.Object[]
            {
                effectManager.bananaQuest,
                effectManager.banditQuest,
                effectManager.boomboxQuest,
                effectManager.bushQuest,
                effectManager.katanaQuest,
                effectManager.luckTomeQuest,
                effectManager.shotgunQuest,
                effectManager.presentQuest,
                effectManager.frogQuest1,
                effectManager.frogQuest2,
                effectManager.frogQuest3,
            })
            {
                if (quest is not null)
                {
                    questPrefabs.Add(quest.Ptr());
                }
            }

            if (effectManager.desertGraves != null)
            {
                foreach (var grave in effectManager.desertGraves)
                {
                    if (grave is not null)
                    {
                        desertGravePrefabs.Add(grave.Ptr());
                    }
                }
            }

            prefabSetsOwner = owner;
        }

        /// <summary>
        /// Only the server should be allowed to spawn chests unless explicitly allowed
        /// </summary>

        [HarmonyPrefix]
        [HarmonyPatch(nameof(UnityEngine.Object.Instantiate), [typeof(UnityEngine.Object), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion)])]
        public static bool Instantiate_Postfix(UnityEngine.Object original, Vector3 position, Quaternion rotation)
        {
            if (!synchronizationService.HasNetplaySessionStarted()) //Checked first: outside a session nothing below matters
            {
                return true;
            }

            var effectManager = GetEffectManager();
            if ((effectManager != null && IsSameObject(original, effectManager.openChestNormal)) || GetOriginalName(original).Contains("OpenChest"))
            {
                var isServer = synchronizationService.IsServerMode();
                if (isServer.HasValue && isServer.Value)
                {
                    return true;
                }

                return Plugin.CAN_SPAWN_CHESTS;
            }

            return true;
        }


        /// <summary>
        /// Deal with boss spawner instantiation on ProceduralMesh map
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(UnityEngine.Object.Instantiate), [typeof(UnityEngine.Object), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion)])]
        public static bool Instantiate_Prefix(UnityEngine.Object original, Vector3 position, Quaternion rotation)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                if (GetOriginalName(original).Contains("BossSpawner"))
                {
                    return false; //Will be sent by the server later and client spawn without position/rotation
                }
            }

            return true;
        }

        /// <summary>
        /// Send chest spawn info when server. Also, the server register what final boss orbs are spawned (sent later on each SpawnOrb postfix)
        /// Also send BossSpawner info when on procedural map
        /// For clients, register the spawned chest received from the server
        /// </summary>

        [HarmonyPostfix]
        [HarmonyPatch(nameof(UnityEngine.Object.Instantiate), [typeof(UnityEngine.Object), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion)])]
        public static void Instantiate_Postfix(Il2CppObjectBase __result, UnityEngine.GameObject original, Vector3 position, Quaternion rotation)
        {
            // Name read by a prefix for this call, if any: taken and cleared here, the call is over
            var cachedOriginalName = original is not null && currentOriginalPointer == original.Ptr() ? currentOriginalName : null;
            currentOriginalPointer = IntPtr.Zero;
            currentOriginalName = null;

            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            var effectManager = GetEffectManager();
            var isServer = synchronizationService.IsServerMode() ?? false;
            if (isServer)
            {
                HandleQuestObjectsSpawned(effectManager, original, __result);
                HandleBossSpawnerOnProceduralMap(__result);
                HandleDesertGraves(effectManager, original, __result);
            }

            if ((effectManager != null && IsSameObject(original, effectManager.openChestNormal)) || (cachedOriginalName ?? original.name).Contains("OpenChest"))
            {
                if (__result == null)
                {
                    return;
                }

                var resultAsObj = IL2CPP.PointerToValueGeneric<UnityEngine.Object>(__result.Pointer, false, false);

                if (isServer)
                {
                    synchronizationService.OnSpawnedChest(position, rotation, resultAsObj);
                }
                else
                {
                    if (Plugin.CAN_SPAWN_CHESTS)
                    {
                        chestManagerService.SetNextChest(resultAsObj);
                    }
                }
            }

            var musicController = MusicController.Instance;
            var finalFightController = IsAlive(musicController) ? musicController.finalFightController : null;
            if (IsAlive(finalFightController))
            {
                if (IsSameObject(original, finalFightController.orbBleed))
                {
                    var nextTarget = finalOrbManagerService.PeakNextTarget();
                    if (nextTarget != null)
                    {
                        (uint nextTargetId, uint orbId) = nextTarget;
                        var resultAsGameObject = IL2CPP.PointerToValueGeneric<GameObject>(__result.Pointer, false, false);
                        finalOrbManagerService.SetOrbTarget(nextTargetId, resultAsGameObject, orbId);
                    }
                }

                if (IsSameObject(original, finalFightController.orbFollowing))
                {
                    var nextTarget = finalOrbManagerService.PeakNextTarget();
                    if (nextTarget != null)
                    {
                        (uint nextTargetId, uint orbId) = nextTarget;
                        var resultAsGameObject = IL2CPP.PointerToValueGeneric<GameObject>(__result.Pointer, false, false);
                        finalOrbManagerService.SetOrbTarget(nextTargetId, resultAsGameObject, orbId);
                    }
                }

                if (IsSameObject(original, finalFightController.orbShooty))
                {
                    var nextTarget = finalOrbManagerService.PeakNextTarget();
                    if (nextTarget != null)
                    {
                        (uint nextTargetId, uint orbId) = nextTarget;

                        var resultAsGameObject = IL2CPP.PointerToValueGeneric<GameObject>(__result.Pointer, false, false);
                        finalOrbManagerService.SetOrbTarget(nextTargetId, resultAsGameObject, orbId);
                    }
                }
            }
        }

        private static void HandleBossSpawnerOnProceduralMap(Il2CppObjectBase result)
        {
            if (MapController.runConfig.mapData.mapType == Assets.Scripts._Data.MapsAndStages.EMapType.ProceduralMesh) //Desert Map handles portals differently
            {
                var resultAsGameObject = IL2CPP.PointerToValueGeneric<GameObject>(result.Pointer, false, false);

                if (resultAsGameObject.name.StartsWith("BossSpawner"))
                {
                    var dynamic = DynamicData.For(resultAsGameObject);
                    var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                    if (!hasBeenSet.HasValue)
                    {
                        synchronizationService.OnSpawnedObject(resultAsGameObject);
                        dynamic.Set("hasBeenSetByServer", true);
                    }
                }
            }
        }

        private static void HandleQuestObjectsSpawned(EffectManager effectManager, UnityEngine.GameObject original, Il2CppObjectBase result)
        {
            if (effectManager == null)
            {
                return;
            }

            EnsurePrefabSets(effectManager);
            if (original is not null && questPrefabs.Contains(original.Ptr()))
            {
                var resultAsGameObject = IL2CPP.PointerToValueGeneric<GameObject>(result.Pointer, false, false);

                //if (EffectManager.Instance.bushQuest == original)
                //{
                //    resultAsGameObject.transform.position = GameManager.Instance.player.transform.position + GameManager.Instance.player.transform.forward * 2f;
                //}

                synchronizationService.OnSpawnedObject(resultAsGameObject);
            }
        }

        private static void HandleDesertGraves(EffectManager effectManager, GameObject original, Il2CppObjectBase result)
        {
            if (effectManager == null)
            {
                return;
            }

            EnsurePrefabSets(effectManager);
            if (Plugin.CAN_SEND_MESSAGES && original is not null && desertGravePrefabs.Contains(original.Ptr()))
            {
                var resultAsGameObject = IL2CPP.PointerToValueGeneric<GameObject>(result.Pointer, false, false);
                synchronizationService.OnSpawnedObject(resultAsGameObject);
            }
        }

        /// <summary>
        /// Synchronize final boss orb destruction (client and server)
        /// </summary>
        /// <param name="obj"></param>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(UnityEngine.Object.Destroy), [typeof(UnityEngine.Object)])]
        public static void Destroy_Postfix(Il2CppObjectBase obj)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            // Called for every destroyed object: nothing to do unless final boss orbs exist and this is one of them
            if (obj is null || !finalOrbManagerService.MayHaveOrbs)
            {
                return;
            }

            var resultAsObj = IL2CPP.PointerToValueGeneric<GameObject>(obj.Pointer, false, false);

            if (finalOrbManagerService.ContainsOrbTarget(resultAsObj) && resultAsObj != null)
            {
                var removed = finalOrbManagerService.RemoveOrbTarget(resultAsObj);

                if (removed.HasValue)
                {
                    synchronizationService.OnFinalBossOrbDestroyed(removed.Value);
                }
            }

        }
    }
}
