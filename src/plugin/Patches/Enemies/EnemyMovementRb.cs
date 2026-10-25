using Actors.Enemies;
using HarmonyLib;
using Megaphonk.Helpers;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace Megaphonk.Patches.Enemies
{
    [HarmonyPatch(typeof(EnemyMovementRb))]
    internal class EnemyMovementRbPatch
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();
        private static readonly Services.IPlayerManagerService playerManagerService = Plugin.Services.GetService<Services.IPlayerManagerService>();

        /// <summary>
        /// Enemies will update their target position each Update
        /// We ensure to target the correct player (local or remote)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyMovementRb.GetTargetPosition))]
        public static bool GetTargetPosition_PostFix(ref Vector3 __result, EnemyMovementRb __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var enemy = __instance.enemy;
            var id = EnemyTargets.Get(enemy);

            if (id.HasValue && (playerManagerService.IsDead(id.Value) || playerManagerService.IsDowned(id.Value))) //Never chase a dead or downed player
            {
                var aliveTarget = playerManagerService.GetRandomPlayerAliveConnectionId();
                if (aliveTarget.HasValue)
                {
                    EnemyTargets.Set(enemy, aliveTarget.Value);
                    id = aliveTarget;
                }
            }

            if (id != null && id.HasValue)
            {
                // Called per enemy per physics step: the target transform is cached per player. Not while a position request
                // is pending, the transform getter patch may then answer with another player's transform
                if (!playerManagerService.PeakNetplayerPositionRequest().HasValue)
                {
                    var targetTransform = playerManagerService.GetTargetTransform(id.Value);
                    if (targetTransform is not null)
                    {
                        __result = targetTransform.position;
                        return false;
                    }
                }

                var localPlayer = playerManagerService.GetLocalPlayer();

                if (localPlayer.ConnectionId == id.Value)
                {
                    var playerGameObject = GameManager.Instance.player.gameObject;
                    var rigidbody = playerGameObject.GetComponent<Rigidbody>();
                    __result = rigidbody.transform.position;
                    return false;
                }

                var netPlayer = playerManagerService.GetNetPlayerByNetplayId(id.Value);
                if (netPlayer == null) //The player might have disconnected or something
                {
                    return true;
                }
                __result = netPlayer.Model.transform.position;

                return false;
            }

            return true;
        }


        /// <summary>
        /// Only the server is allowed to update enemy movement
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyMovementRb.FindNextPosition))]
        public static bool FindNextPosition_Prefix(EnemyMovementRb __instance)
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

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyMovementRb.MyFixedUpdate))]
        public static bool MyFixedUpdate_Prefix(EnemyMovementRb __instance)
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

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyMovementRb.TryClimbWall))]
        public static bool TryClimbWall_Prefix(EnemyMovementRb __instance)
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

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyMovementRb.CheckGrounded))]
        public static bool CheckGrounded_Prefix(EnemyMovementRb __instance)
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

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyMovementRb.DashStart))]
        public static bool DashStart_Prefix(EnemyMovementRb __instance)
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

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyMovementRb.StartMovement))]
        public static bool StartMovement_Prefix(EnemyMovementRb __instance)
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
