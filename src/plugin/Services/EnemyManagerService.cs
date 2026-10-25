using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Megaphonk.Common.Messages;
using Megaphonk.Helpers;
using Megaphonk.Scripts.Enemies;
using Megaphonk.Scripts.Snapshot;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Megaphonk.Services
{
    public interface IEnemyManagerService
    {
        public IEnumerable<(uint, uint)> ReTargetEnemies(uint oldTargetId, IEnumerable<uint> currentPlayersExcludingOldOneId);
        /// <summary>
        /// Host, once per enemy tick: the enemies to send. <paramref name="referencePositions"/> are where the remote players
        /// look from (distance tiers). Empty: every enemy is sent as near.
        /// </summary>
        public List<EnemySnapshotEntry> GetAllEnemiesDeltaAndUpdate(List<System.Numerics.Vector3> referencePositions);
        /// <summary>
        /// Host, any thread: a client lost a snapshot packet with these enemies, send them absolute next tick
        /// </summary>
        public void MarkSendsLost(IEnumerable<uint> enemyIds);
        public uint AddSpawnedEnemy(Enemy enemy);
        public void SetSpawnedEnemy(uint enemyId, Enemy enemy, EnemyInterpolator interpolator = null);
        public Enemy GetEnemyById(uint id);
        public EnemyInterpolator GetEnemyInterpolatorById(uint id);
        public KeyValuePair<uint, Enemy> GetEnemyByReference(Enemy enemy);
        public void RemoveEnemyById(uint id);
        public void ResetForNextLevel();
        public void ApplyRetargetedEnemies(IEnumerable<(uint, uint)> enemy_NewTargetids, IEnumerable<(uint, Rigidbody)> playerId_rigidbody);
        public void InitializeSwitcher(TargetSwitcher switcher, EEnemyFlag enemyFlag, EEnemy enemyName);

        public void AddReviverEnemy_Name(Enemy enemy, string netplayName);
        public string GetReviverEnemy_Name(Enemy enemy);
        public void RemoveReviverEnemy_Name(Enemy enemy);
        public void RebalanceIfNeededReviverEnemy(Enemy enemy, uint? currentReviver, uint? currentReviverOwner);
        public void ResetReviverSpawnCounts();
    }
    internal class EnemyManagerService : IEnemyManagerService
    {
        /// <summary>
        /// Host snapshot state of one enemy: its transform (fetched once) and what the clients were last sent for it
        /// </summary>
        private class EnemySyncState : EnemySendState
        {
            public Transform Transform;
        }

        private readonly ConcurrentDictionary<uint, Enemy> spawnedEnemies = [];
        // Enemy native pointer -> network id (was a DynamicData field)
        private readonly ConcurrentDictionary<System.IntPtr, uint> enemyIdsByPointer = [];
        // Client: interpolator of each spawned enemy, so snapshots don't call GetComponent per enemy
        private readonly ConcurrentDictionary<uint, EnemyInterpolator> enemyInterpolators = [];
        // Host snapshot states (main thread only)
        private readonly Dictionary<uint, EnemySyncState> syncStates = [];
        private readonly List<uint> staleSyncStates = [];
        private long snapshotTick = 0;
        // Host: enemies whose last sends a client reported lost (filled from the network thread)
        private readonly ConcurrentQueue<uint> lostEnemySends = new();
        private readonly ConcurrentDictionary<Enemy, string> reviverEnemies_NetplayNames = [];
        private readonly ConcurrentDictionary<uint, int> reviverSpawnCountPerOwner = [];
        private uint currentEnemyId = 0; //TODO: concurrency?

        /// <summary>
        /// Server side, retarget enemies when a player dies (or other use case ? )
        /// </summary>
        public IEnumerable<(uint, uint)> ReTargetEnemies(uint oldTargetId, IEnumerable<uint> currentPlayersAliveExcludingOldOneId)
        {
            var retargetedEnemies = new List<(uint, uint)>();
            currentPlayersAliveExcludingOldOneId = currentPlayersAliveExcludingOldOneId.ToList();
            if (!currentPlayersAliveExcludingOldOneId.Any())
            {
                return retargetedEnemies; //Last player alive died: nobody to retarget to
            }

            var oldTargetEnemies = spawnedEnemies.Values.Where(enemy =>
            {
                var currentTargetid = EnemyTargets.Get(enemy);
                if (currentTargetid.HasValue && currentTargetid.Value == oldTargetId)
                {
                    return true;
                }
                return false;
            });


            foreach (var oldEnemy in oldTargetEnemies)
            {
                var randomIndex = Random.Range(0, currentPlayersAliveExcludingOldOneId.Count());
                var randomNewTargetId = currentPlayersAliveExcludingOldOneId.ElementAt(randomIndex);

                EnemyTargets.Set(oldEnemy, randomNewTargetId);
                var enemyId = GetEnemyByReference(oldEnemy).Key;

                retargetedEnemies.Add((enemyId, randomNewTargetId));
            }

            return retargetedEnemies;
        }

        public void ApplyRetargetedEnemies(IEnumerable<(uint, uint)> enemy_NewTargetids, IEnumerable<(uint, Rigidbody)> playerId_rigidbody)
        {
            foreach (var (enemyId, newTargetId) in enemy_NewTargetids)
            {
                var enemy = GetEnemyById(enemyId);
                if (enemy != null)
                {
                    var playerRigidbody = playerId_rigidbody.FirstOrDefault(pr => pr.Item1 == newTargetId).Item2;
                    if (playerRigidbody != null)
                    {
                        EnemyTargets.Set(enemy, newTargetId);
                        enemy.target = playerRigidbody;
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"Failed to retarget enemy {enemyId} to new target {newTargetId} - enemy not found");
                }
            }
        }

        /// <summary>
        /// This should be called once per server tick. Clients extrapolate each enemy from its last sent position and velocity:
        /// only the enemies that drift from that prediction (or change velocity, yaw, hp, or are due a keyframe) are sent,
        /// see EnemySnapshotPlanner. Enemies far from every remote player are only read every 4th/8th tick (staggered by id).
        /// </summary>
        public List<EnemySnapshotEntry> GetAllEnemiesDeltaAndUpdate(List<System.Numerics.Vector3> referencePositions)
        {
            snapshotTick++;
            var realTime = Time.realtimeSinceStartup;
            var gameTime = Scripts.NetworkHandler.EnemyClock; //Clients extrapolate on the same clock: a pause stops both
            var paused = Scripts.NetworkHandler.IsGamePaused;

            // Enemies a client lost a packet of (acks): next send absolute
            while (lostEnemySends.TryDequeue(out var lostId))
            {
                if (syncStates.TryGetValue(lostId, out var lostState) && lostState.HasSent)
                {
                    lostState.ForceAbsolute = true;
                }
            }
            var frame = Quantizer.GetFrame();
            var deltas = new List<EnemySnapshotEntry>();
            var count = 0;

            foreach (var (id, enemy) in spawnedEnemies)
            {
                count++;
                if (!syncStates.TryGetValue(id, out var state))
                {
                    state = new EnemySyncState();
                    syncStates[id] = state;
                }
                state.LastTick = snapshotTick;

                if (!EnemySnapshotPlanner.IsReadDue(state, snapshotTick, id, realTime, out var keyframe, out var absolute, out var positionDue))
                {
                    continue;
                }

                var hp = enemy.hp;
                var hpChanged = EnemySnapshotPlanner.HasHpChanged(state, hp);
                if (!positionDue && !hpChanged)
                {
                    continue;
                }

                var transform = state.Transform;
                if (transform is null || transform.CachedPtr() == System.IntPtr.Zero)
                {
                    transform = enemy.transform;
                    state.Transform = transform;
                }

                var position = transform.position;
                var yaw = Quantizer.QuantizeYawByte(transform.eulerAngles.y);
                if (EnemySnapshotPlanner.TryPlan(state, id, new System.Numerics.Vector3(position.x, position.y, position.z), yaw, hp,
                    keyframe, absolute, hpChanged, paused, gameTime, realTime, frame, referencePositions, out var entry))
                {
                    deltas.Add(entry);
                }
            }

            // Enemies no longer spawned: forget what was sent for them
            if (syncStates.Count > count)
            {
                foreach (var (id, state) in syncStates)
                {
                    if (state.LastTick != snapshotTick)
                    {
                        staleSyncStates.Add(id);
                    }
                }

                foreach (var id in staleSyncStates)
                {
                    syncStates.Remove(id);
                }

                staleSyncStates.Clear();
            }

            return deltas;
        }

        public Enemy GetEnemyById(uint id)
        {
            if (spawnedEnemies.TryGetValue(id, out var enemy))
            {
                return enemy;
            }
            return null;
        }


        /// <summary>
        /// Server side
        /// </summary>
        public uint AddSpawnedEnemy(Enemy enemy)
        {
            currentEnemyId++;
            if (!spawnedEnemies.TryAdd(currentEnemyId, enemy))
            {
                Plugin.Log.LogWarning($"Attempted to add an enemy that already exists. EnemyId: {currentEnemyId}");
                return 0;
            }

            enemyIdsByPointer[enemy.Ptr()] = currentEnemyId;
            syncStates[currentEnemyId] = new EnemySyncState { Transform = enemy.CachedPtr() != System.IntPtr.Zero ? enemy.transform : null };

            return currentEnemyId;
        }

        /// <summary>
        /// Client side
        /// </summary>
        public void SetSpawnedEnemy(uint enemyId, Enemy enemy, EnemyInterpolator interpolator = null)
        {
            if (!spawnedEnemies.TryAdd(enemyId, enemy))
            {
                Plugin.Log.LogWarning($"Attempted to add an enemy that already exists. EnemyId: {enemyId}");
                return;
            }

            enemyIdsByPointer[enemy.Ptr()] = enemyId;
            if (interpolator is not null)
            {
                enemyInterpolators[enemyId] = interpolator;
            }
        }

        public EnemyInterpolator GetEnemyInterpolatorById(uint id)
        {
            return enemyInterpolators.TryGetValue(id, out var interpolator) ? interpolator : null;
        }

        /// <summary>
        /// Compared by pointer: Unity's == is a native call, and this runs for every damage
        /// </summary>
        public KeyValuePair<uint, Enemy> GetEnemyByReference(Enemy enemy)
        {
            if (enemy is null)
            {
                return default;
            }

            var pointer = enemy.Ptr();
            if (enemyIdsByPointer.TryGetValue(pointer, out var netplayId)
                && spawnedEnemies.TryGetValue(netplayId, out var stored)
                && stored is not null
                && stored.Ptr() == pointer)
            {
                return new KeyValuePair<uint, Enemy>(netplayId, stored);
            }

            foreach (var kv in spawnedEnemies)
            {
                if (kv.Value is not null && kv.Value.Ptr() == pointer)
                {
                    return kv;
                }
            }

            return default;
        }

        public void RemoveEnemyById(uint id)
        {
            if (!spawnedEnemies.TryRemove(id, out var enemy))
            {
                return;
            }

            if (enemy is not null)
            {
                enemyIdsByPointer.TryRemove(new KeyValuePair<System.IntPtr, uint>(enemy.Ptr(), id)); //Only if still mapped to this id
            }
            enemyInterpolators.TryRemove(id, out _);
            syncStates.Remove(id);
        }

        public void MarkSendsLost(IEnumerable<uint> enemyIds)
        {
            foreach (var id in enemyIds)
            {
                lostEnemySends.Enqueue(id);
            }
        }

        public void ResetForNextLevel()
        {
            //spawnedEnemies.Select(Enemy => Enemy.Value).ToList().ForEach(enemy => GameObject.Destroy(enemy.gameObject));
            spawnedEnemies.Clear();
            enemyIdsByPointer.Clear();
            enemyInterpolators.Clear();
            syncStates.Clear();
        }

        //TODO: the applied values should be stored in GameBalanceService
        public void InitializeSwitcher(TargetSwitcher switcher, EEnemyFlag enemyFlag, EEnemy enemyName)
        {
            switch (enemyName)
            {
                case EEnemy.GhostGrave1:
                case EEnemy.GhostGrave2:
                case EEnemy.GhostGrave3:
                case EEnemy.GhostGrave4:
                    switcher.UpdateSwitchIntervalRange(7f, 12f);
                    switcher.UpdateSwitchMaxDistance(30f);
                    return;
                default:
                    break;
            }

            switch (enemyFlag)
            {
                case EEnemyFlag.FinalBoss:
                    switcher.UpdateSwitchIntervalRange(30f, 50f);
                    switcher.UpdateSwitchMaxDistance(300f);
                    break;
                case EEnemyFlag.StageBoss:
                    switcher.UpdateSwitchIntervalRange(20f, 40f);
                    switcher.UpdateSwitchMaxDistance(300f);
                    break;
                default:
                    switcher.UpdateSwitchIntervalRange(40f, 60f);
                    switcher.UpdateSwitchMaxDistance(50f);
                    break;
            }
        }

        public void AddReviverEnemy_Name(Enemy enemy, string netplayName)
        {
            reviverEnemies_NetplayNames.TryAdd(enemy, netplayName);
        }

        public string GetReviverEnemy_Name(Enemy enemy)
        {
            if (reviverEnemies_NetplayNames.TryGetValue(enemy, out var name))
            {
                return name;
            }
            return null;
        }

        public void RemoveReviverEnemy_Name(Enemy enemy)
        {
            reviverEnemies_NetplayNames.TryRemove(enemy, out _);
        }

        public void RebalanceIfNeededReviverEnemy(Enemy enemy, uint? currentReviver, uint? currentReviverOwner)
        {
            if (!currentReviver.HasValue || !currentReviverOwner.HasValue)
            {
                return;
            }

            var ownerId = currentReviverOwner.Value;
            var count = reviverSpawnCountPerOwner.AddOrUpdate(ownerId, 1, (_, prev) => prev + 1);

            if (count >= 6)
            {
                return;
            }

            var multiplier = (count * 2) / 12f;
            var newHp = enemy.hp * multiplier;

            enemy.hp = newHp;
            enemy.controlHp = newHp;
            enemy.maxHp = newHp;
            enemy._hp_k__BackingField = newHp;
        }

        public void ResetReviverSpawnCounts()
        {
            reviverSpawnCountPerOwner.Clear();
        }
    }
}
