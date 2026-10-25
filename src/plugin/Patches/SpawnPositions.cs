using Assets.Scripts.Game.Spawning;
using HarmonyLib;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Megaphonk.Patches
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
        public static void GetEnemySpawnPosition_Postfix(ref UnityEngine.Vector3 __result)
        {
            try
            {
                PushSpawnAway(ref __result);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error in SpawnPositionsPatch.PushSpawnAway: {ex}");
            }

            if (playerManagerService.TryGetGetNetplayerPosition(out uint id))
            {
                PendingSpawnTargetId = id;
                PendingSpawnFrame = UnityEngine.Time.frameCount;
            }
        }

        // Enemies spawn this many times further from the player than the game picks: a fast player no longer reaches them
        // during their spawn animation. On a narrow map the spawn comes back towards the player to the last ground found
        private const float SPAWN_DISTANCE_MULTIPLIER = 2f;
        // The game teleports enemies further than 87.5 m (Enemy.startTeleportThresholdDistance) next to the player: stay below
        private const float MAX_SPAWN_DISTANCE = 80f;
        private const int SPAWN_DISTANCE_STEPS = 10;
        private const float GROUND_RAY_HEIGHT = 80f;
        private const float GROUND_RAY_LENGTH = 250f;
        private static int groundRayMask;
        private static int groundLayer = -1;

        /// <summary>
        /// Move the spawn position chosen by the game SPAWN_DISTANCE_MULTIPLIER times as far from the player it was chosen
        /// around (at most MAX_SPAWN_DISTANCE), onto the ground found below. No ground there (off the map, water, no spawn area): a bit closer each step,
        /// down to the game's own position.
        /// </summary>
        private static void PushSpawnAway(ref UnityEngine.Vector3 position)
        {
            if (position == SpawnPositions.INVALID_POS)
            {
                return;
            }

            var center = GetSpawnCenter();
            if (!center.HasValue)
            {
                return;
            }

            if (groundLayer < 0)
            {
                groundLayer = UnityEngine.LayerMask.NameToLayer(SpawnPositions.layerGround);
                groundRayMask = LayerBit(groundLayer) | LayerBit(UnityEngine.LayerMask.NameToLayer(SpawnPositions.layerWater))
                    | LayerBit(UnityEngine.LayerMask.NameToLayer(SpawnPositions.layerNoSpawnArea)); //LayerMask.GetMask is stripped
            }
            if (groundLayer < 0)
            {
                return;
            }

            var offset = position - center.Value;
            offset.y = 0f;
            var distance = offset.magnitude;
            if (distance < 0.01f)
            {
                return;
            }

            var maxFactor = UnityEngine.Mathf.Min(SPAWN_DISTANCE_MULTIPLIER, MAX_SPAWN_DISTANCE / distance);
            if (maxFactor <= 1f)
            {
                return;
            }

            var rayTop = UnityEngine.Mathf.Max(center.Value.y, position.y) + GROUND_RAY_HEIGHT;
            for (var step = 0; step < SPAWN_DISTANCE_STEPS; step++)
            {
                var factor = maxFactor - (maxFactor - 1f) * step / SPAWN_DISTANCE_STEPS;
                var candidate = center.Value + offset * factor;
                var origin = new UnityEngine.Vector3(candidate.x, rayTop, candidate.z);
                if (UnityEngine.Physics.Raycast(origin, UnityEngine.Vector3.down, out var hit, GROUND_RAY_LENGTH, groundRayMask, UnityEngine.QueryTriggerInteraction.Collide)
                    && hit.collider.gameObject.layer == groundLayer
                    && (hit.point - center.Value).sqrMagnitude <= MAX_SPAWN_DISTANCE * MAX_SPAWN_DISTANCE)
                {
                    position = hit.point;
                    return;
                }
            }
        }

        private static int LayerBit(int layer)
        {
            return layer >= 0 ? 1 << layer : 0;
        }

        /// <summary>
        /// The player the spawn position was chosen around: the netplay player picked by the prefix, else the local one
        /// </summary>
        private static UnityEngine.Vector3? GetSpawnCenter()
        {
            var netplayerId = synchronizationService.HasNetplaySessionStarted() ? playerManagerService.PeakNetplayerPosition() : null;
            if (netplayerId.HasValue && playerManagerService.IsRemoteConnectionId(netplayerId.Value))
            {
                var netPlayer = playerManagerService.GetNetPlayerByNetplayId(netplayerId.Value);
                return netPlayer?.Model != null ? netPlayer.Model.transform.position : null;
            }

            var player = GameManager.Instance?.player;
            return player != null ? player.transform.position : null;
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
