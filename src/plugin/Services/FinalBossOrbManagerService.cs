using Megaphonk.Common.Models;
using Megaphonk.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Megaphonk.Services
{
    public interface IFinalBossOrbManagerService
    {
        public void QueueNextTarget(uint targetId);
        public Tuple<uint, uint> PeakNextTarget();
        public Tuple<uint, uint> GetNextTargetAndOrbId();
        public void SetOrbTarget(uint targetId, GameObject target, uint orbId);

        public uint? RemoveOrbTarget(GameObject go);

        public bool ContainsOrbTarget(GameObject go);

        /// <summary>
        /// False when no orb was registered since the last reset (cheap early out for the Object.Destroy patch)
        /// </summary>
        public bool MayHaveOrbs { get; }

        public void Reset();
        public IEnumerable<BossOrbModel> GetAllOrbs();
        public GameObject GetOrbById(uint id);
        public uint? GetTargetIdByReference(GameObject go);

        public void ClearQueueNextTarget();
    }

    internal class FinalBossOrbManagerService : IFinalBossOrbManagerService //TODO: Simplify this zzz
    {
        private class OrbInfo
        {
            public uint OrbId { get; set; }
            public uint TargetId { get; set; }
            public GameObject GameObject { get; set; }
        }

        private readonly ConcurrentDictionary<uint, OrbInfo> _orbsById = [];
        private readonly ConcurrentQueue<uint> _queuedTargetIds = [];
        private readonly ConcurrentQueue<(uint targetId, uint orbId)> _pendingOrbCreation = [];
        private uint _nextOrbId = 0;
        private volatile bool _mayHaveOrbs = false;

        public bool MayHaveOrbs => _mayHaveOrbs;

        public void QueueNextTarget(uint targetId)
        {
            _queuedTargetIds.Enqueue(targetId);
        }

        public void ClearQueueNextTarget()
        {
            _queuedTargetIds.Clear();
        }

        /// <summary>
        /// Peak and reserve the next target for orb creation, can be called multiple times before GetNextTargetAndOrbId
        /// </summary>
        /// <returns></returns>
        public Tuple<uint, uint> PeakNextTarget()
        {
            if (!_queuedTargetIds.TryPeek(out var targetId))
                return null;

            var orbId = ++_nextOrbId;
            _pendingOrbCreation.Enqueue((targetId, orbId));
            return Tuple.Create(targetId, orbId);
        }

        /// <summary>
        /// Get and remove the next target for orb creation, should be called multiple times after PeakNextTarget
        /// </summary>
        /// <returns></returns>
        public Tuple<uint, uint> GetNextTargetAndOrbId()
        {
            if (_pendingOrbCreation.TryDequeue(out var pending))
            {
                return Tuple.Create(pending.targetId, pending.orbId);
            }

            return null;
        }

        public void SetOrbTarget(uint targetId, GameObject target, uint orbId)
        {
            _orbsById[orbId] = new OrbInfo
            {
                OrbId = orbId,
                TargetId = targetId,
                GameObject = target
            };
            _mayHaveOrbs = true;
        }

        public void Reset()
        {
            _mayHaveOrbs = false;
            _orbsById.Clear();
            _queuedTargetIds.Clear();
            _pendingOrbCreation.Clear();
            _nextOrbId = 0;
        }

        public bool ContainsOrbTarget(GameObject go)
        {
            return FindOrb(go) != null;
        }

        /// <summary>
        /// Compared by pointer (Unity's == is a native call), without copying the values
        /// </summary>
        private OrbInfo FindOrb(GameObject go)
        {
            if (go is null)
            {
                return null;
            }

            var pointer = go.Ptr();
            foreach (var (_, orb) in _orbsById)
            {
                if (orb.GameObject is not null && orb.GameObject.Ptr() == pointer)
                {
                    return orb;
                }
            }

            return null;
        }

        public IEnumerable<BossOrbModel> GetAllOrbs()
        {
            return _orbsById.Values
                .Where(orb => orb.GameObject != null)
                .Select(orb => new BossOrbModel
                {
                    Id = orb.OrbId,
                    Position = Quantizer.Quantize(orb.GameObject.transform.position)
                });
        }

        public GameObject GetOrbById(uint id)
        {
            return _orbsById.TryGetValue(id, out var orb) ? orb.GameObject : null;
        }

        public uint? RemoveOrbTarget(GameObject go)
        {
            var orbToRemove = FindOrb(go);

            if (orbToRemove == null)
                return null;

            _orbsById.TryRemove(orbToRemove.OrbId, out _);
            return orbToRemove.OrbId;
        }

        public uint? GetTargetIdByReference(GameObject go)
        {
            var orb = FindOrb(go);
            return orb?.TargetId;
        }
    }
}
