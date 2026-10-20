using Assets.Scripts.Game.Spawning;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(SpawnPositions))]
    internal class SpawnPositionsPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// Randomly select a player to get the enemy spawn position
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnPositions.GetEnemySpawnPosition))]
        public static void GetEnemySpawnPosition_Prefix()
        {
            try
            {

                if (!synchronizationService.HasNetplaySessionStarted() || synchronizationService.IsLoadingNextLevel())
                {
                    return;
                }

                var isServer = synchronizationService.IsServerMode();
                if (isServer.HasValue && isServer.Value)
                {
                    var randomPlayerId = playerManagerService.GetRandomPlayerAliveConnectionId();

                    if (randomPlayerId.HasValue)
                    {
                        playerManagerService.AddGetNetplayerPosition(randomPlayerId.Value);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error in SpawnPositionsPatch.GetEnemySpawnPosition_Prefix: {ex}");
            }
        }

        /// <summary>
        /// The chosen player's position is only needed while the spawn position is computed: stop redirecting
        /// Transform.position right away and keep the player for the enemy about to be spawned (its target).
        /// Left queued, a spawn that didn't happen (no valid position) kept redirecting every position read until the next spawn.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnPositions.GetEnemySpawnPosition))]
        public static void GetEnemySpawnPosition_Postfix()
        {
            if (playerManagerService.TryGetGetNetplayerPosition(out uint id))
            {
                PendingSpawnTargetId = id;
                PendingSpawnFrame = UnityEngine.Time.frameCount;
            }
        }

        private static uint? PendingSpawnTargetId;
        private static int PendingSpawnFrame;

        /// <summary>
        /// Player the enemy being initialized spawned next to, if its position was computed this frame
        /// </summary>
        public static uint? TakeSpawnTarget()
        {
            var id = PendingSpawnFrame == UnityEngine.Time.frameCount ? PendingSpawnTargetId : null;
            PendingSpawnTargetId = null;
            return id;
        }
    }
}
