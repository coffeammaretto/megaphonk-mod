using Assets.Scripts.Inventory__Items__Pickups.Weapons.Projectiles;
using Megaphonk.Common.Models;
using Megaphonk.Extensions;
using Megaphonk.Helpers;
using Megaphonk.Scripts.Snapshot;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Megaphonk.Services
{
    public interface IProjectileManagerService
    {
        public IEnumerable<Projectile> GetAllProjectiles();
        public List<Projectile> GetAllProjectilesDeltaAndUpdate();
        /// <summary>
        /// The next snapshot holds every projectile
        /// </summary>
        public void ResendAll();
        public uint AddSpawnedProjectile(ProjectileBase projectile);
        public ProjectileBase GetProjectileById(uint id);
        public KeyValuePair<uint, ProjectileBase> GetProjectileByReference(ProjectileBase projectile);
        public ProjectileBase RemoveProjectileById(uint id);
        public void ResetForNextLevel();
        public void RemoveProjectile(Projectile projectileId);
        public void RegisterProjectileForInterpolation(uint id, GameObject projectile);
        public bool UnregisterProjectileFromInterpolation(uint id);
        public void UpdateProjectileSnapshots(List<ProjectileSnapshot> projectileSnapshots);
        public void RemoveProjectilesByOwnerId(uint connectionId);
    }

    internal class ProjectileManagerService : IProjectileManagerService
    {
        /// <summary>
        /// Host snapshot state of one projectile: its synchronized transform (resolved once) and the position it had on the
        /// previous tick, already quantized
        /// </summary>
        private class ProjectileSyncState
        {
            public Transform Transform;
            public bool HasPrevious;
            public short X;
            public short Y;
            public short Z;
            public long LastTick;
        }

        private readonly ConcurrentDictionary<uint, ProjectileBase> spawnedProjectile = [];
        private readonly ConcurrentDictionary<System.IntPtr, uint> projectileIdsByPointer = [];
        private readonly Dictionary<uint, ProjectileSyncState> syncStates = [];
        private readonly List<uint> staleSyncStates = [];
        private int previousTickProjectileCount = 0;
        private long snapshotTick = 0;
        private uint currentProjectileId = 0;
        private ProjectileInterpolator projectileInterpolator;

        private const float POSITION_THRESHOLD = 0.05f;

        public IEnumerable<Projectile> GetAllProjectiles()
        {
            RemoveAllDeadProjectiles();
            return spawnedProjectile.Select(kv => kv.Value.ToModel(kv.Key)).ToList();
        }

        /// <summary>
        /// Projectiles that moved since the previous tick (every projectile when there were none on the previous tick).
        /// Positions are quantized and compared before any model is allocated.
        /// </summary>
        public List<Projectile> GetAllProjectilesDeltaAndUpdate()
        {
            snapshotTick++;
            var sendAll = previousTickProjectileCount == 0;
            var deltas = new List<Projectile>();
            var count = 0;

            foreach (var (id, projectile) in spawnedProjectile)
            {
                count++;
                if (!syncStates.TryGetValue(id, out var state))
                {
                    state = new ProjectileSyncState();
                    syncStates[id] = state;
                }

                var transform = state.Transform;
                if (transform is null || transform.CachedPtr() == System.IntPtr.Zero)
                {
                    try
                    {
                        transform = projectile.GetSnapshotTransform();
                    }
                    catch (System.Exception)
                    {
                        transform = null; //Moving part not set up yet (or projectile gone): skipped this tick, resolved again next tick
                    }

                    state.Transform = transform;
                    if (transform is null || transform.CachedPtr() == System.IntPtr.Zero)
                    {
                        continue;
                    }
                }

                var position = transform.position;
                var x = Quantizer.Quantize(position.x);
                var y = Quantizer.Quantize(position.y);
                var z = Quantizer.Quantize(position.z);

                if (sendAll || !state.HasPrevious || HasDelta(state, x, y, z))
                {
                    var model = new Projectile { Id = id };
                    model.Position.QuantizedX = x;
                    model.Position.QuantizedY = y;
                    model.Position.QuantizedZ = z;
                    model.FordwardVector = Quantizer.Quantize(transform.rotation * Vector3.forward);
                    deltas.Add(model);
                }

                state.HasPrevious = true;
                state.X = x;
                state.Y = y;
                state.Z = z;
                state.LastTick = snapshotTick;
            }

            // Projectiles gone since the previous tick: a later one with the same id must count as new
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

            previousTickProjectileCount = count;

            return deltas;
        }

        private static bool HasDelta(ProjectileSyncState previous, short x, short y, short z)
        {
            float positionDelta = Vector3.Distance(
                new Vector3(Quantizer.Dequantize(previous.X), Quantizer.Dequantize(previous.Y), Quantizer.Dequantize(previous.Z)),
                new Vector3(Quantizer.Dequantize(x), Quantizer.Dequantize(y), Quantizer.Dequantize(z))
            );

            return positionDelta > POSITION_THRESHOLD;
        }

        public ProjectileBase GetProjectileById(uint id)
        {
            if (spawnedProjectile.TryGetValue(id, out var projo))
            {
                return projo;
            }
            return null;
        }

        private void RemoveAllDeadProjectiles()
        {
            var toRemove = spawnedProjectile.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
            foreach (var id in toRemove)
            {
                spawnedProjectile.TryRemove(id, out var _);
            }
        }

        public uint AddSpawnedProjectile(ProjectileBase projectile)
        {
            currentProjectileId++;
            if (!spawnedProjectile.TryAdd(currentProjectileId, projectile))
            {
                Plugin.Log.LogWarning($"Attempted to add a projectile that already exists. Id: {currentProjectileId}");
                return 0;
            }

            projectileIdsByPointer[projectile.Ptr()] = currentProjectileId;

            return currentProjectileId;
        }

        /// <summary>
        /// Looked up by the id stored for its pointer, compared by pointer (Unity's == is a native call)
        /// </summary>
        public KeyValuePair<uint, ProjectileBase> GetProjectileByReference(ProjectileBase projectile)
        {
            if (projectile is null)
            {
                return default;
            }

            var pointer = projectile.Ptr();
            if (projectileIdsByPointer.TryGetValue(pointer, out var id)
                && spawnedProjectile.TryGetValue(id, out var stored)
                && stored is not null
                && stored.Ptr() == pointer)
            {
                return new KeyValuePair<uint, ProjectileBase>(id, stored);
            }

            foreach (var kv in spawnedProjectile)
            {
                if (kv.Value is not null && kv.Value.Ptr() == pointer)
                {
                    return kv;
                }
            }

            return default;
        }

        public ProjectileBase RemoveProjectileById(uint id)
        {
            if (!spawnedProjectile.TryRemove(id, out var projectile))
            {
                Plugin.Log.LogWarning($"Attempted to remove an projectile that does not exist {id}");
                return null;
            }

            if (projectile is not null)
            {
                projectileIdsByPointer.TryRemove(new KeyValuePair<System.IntPtr, uint>(projectile.Ptr(), id)); //Only if still mapped to this id
            }

            return projectile;
        }

        public void ResendAll()
        {
            previousTickProjectileCount = 0;
        }

        public void ResetForNextLevel()
        {
            currentProjectileId = 0;
            spawnedProjectile.Clear();
            projectileIdsByPointer.Clear();
            syncStates.Clear();
            previousTickProjectileCount = 0;

            if (projectileInterpolator != null)
            {
                Object.Destroy(projectileInterpolator.gameObject);
                projectileInterpolator = null;
            }
        }

        public void RemoveProjectile(Projectile projectileId)
        {
            var removed = RemoveProjectileById(projectileId.Id);

            if (removed == null)
            {
                Plugin.Log.LogWarning($"Tried to remove projectile with id {projectileId.Id} but it was not found.");
                return;
            }

            GameObject.Destroy(removed.gameObject);
        }

        public void RegisterProjectileForInterpolation(uint id, GameObject projectile)
        {
            EnsureInterpolatorExists();
            projectileInterpolator.RegisterProjectile(id, projectile);
        }

        public bool UnregisterProjectileFromInterpolation(uint id)
        {
            return projectileInterpolator != null && projectileInterpolator.UnregisterProjectile(id);
        }

        public void UpdateProjectileSnapshots(List<ProjectileSnapshot> projectileSnapshots)
        {
            EnsureInterpolatorExists();

            if (projectileSnapshots != null && projectileSnapshots.Count > 0)
            {
                projectileInterpolator.UpdateProjectiles(projectileSnapshots);
            }
        }

        public void RemoveProjectilesByOwnerId(uint connectionId)
        {
            var projectilesToRemove = spawnedProjectile
                .Where(kv => kv.Value != null && kv.Key == connectionId)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var projectileId in projectilesToRemove)
            {
                var removedProjectile = RemoveProjectileById(projectileId);
                if (removedProjectile != null)
                {
                    GameObject.DestroyImmediate(removedProjectile.gameObject);
                }
            }
        }

        private void EnsureInterpolatorExists()
        {
            if (projectileInterpolator == null)
            {
                var interpolatorGameObject = new GameObject("ProjectileInterpolator");
                projectileInterpolator = interpolatorGameObject.AddComponent<ProjectileInterpolator>();
                Object.DontDestroyOnLoad(interpolatorGameObject);
            }
        }
    }
}
