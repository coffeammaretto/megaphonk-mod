using Megaphonk.Helpers;
using System.Collections.Generic;
using UnityEngine;

namespace Megaphonk.Scripts.Snapshot
{
    public class ProjectileInterpolator : MonoBehaviour
    {
        private readonly Dictionary<uint, GameObject> activeProjectiles = new Dictionary<uint, GameObject>();
        // Interpolated transform of each active projectile, resolved once at registration (three GetComponent calls)
        private readonly Dictionary<uint, ProjectileTarget> activeTransforms = new Dictionary<uint, ProjectileTarget>();
        private readonly Dictionary<uint, List<ProjectileSnapshot>> snapshotsBuffers = new Dictionary<uint, List<ProjectileSnapshot>>();

        protected float interpolationDelayMs = 0.1f;
        protected int maxBufferSize = 30;

        protected void Update()
        {
            double renderTime = Time.timeAsDouble - interpolationDelayMs;

            foreach (var projectileId in activeProjectiles.Keys)
            {
                if (!snapshotsBuffers.TryGetValue(projectileId, out var buffer))
                    continue;

                if (!HasEnoughSnapshots(buffer))
                    continue;

                PerformInterpolation(projectileId, buffer, renderTime);
                CleanupOldSnapshots(buffer, renderTime);
            }
        }

        public void UpdateProjectiles(List<ProjectileSnapshot> projectileSnapshots)
        {
            if (projectileSnapshots == null || projectileSnapshots.Count == 0)
                return;

            foreach (var snapshot in projectileSnapshots)
            {
                AddSnapshot(snapshot);
            }
        }

        private void AddSnapshot(ProjectileSnapshot snapshot)
        {
            if (!snapshotsBuffers.TryGetValue(snapshot.Id, out var buffer))
            {
                buffer = new List<ProjectileSnapshot>();
                snapshotsBuffers[snapshot.Id] = buffer;
            }

            buffer.Add(snapshot);

            if (buffer.Count > maxBufferSize)
            {
                buffer.RemoveAt(0);
            }
        }

        private bool HasEnoughSnapshots(List<ProjectileSnapshot> buffer)
        {
            return buffer.Count >= 2;
        }

        private void PerformInterpolation(uint projectileId, List<ProjectileSnapshot> buffer, double renderTime)
        {
            if (!activeProjectiles.TryGetValue(projectileId, out var projectile)) return;

            if (!FindSnapshotPair(buffer, renderTime, out ProjectileSnapshot older, out ProjectileSnapshot newer)) return;

            float t = CalculateInterpolationFactor(renderTime, older.Timestamp, newer.Timestamp);
            t = Mathf.Clamp01(t);

            InterpolateSnapshot(projectileId, projectile, older, newer, t);
        }

        private bool FindSnapshotPair(List<ProjectileSnapshot> buffer, double renderTime, out ProjectileSnapshot older, out ProjectileSnapshot newer)
        {
            older = null;
            newer = null;

            for (int i = 0; i < buffer.Count - 1; i++)
            {
                if (buffer[i].Timestamp <= renderTime &&
                    buffer[i + 1].Timestamp >= renderTime)
                {
                    older = buffer[i];
                    newer = buffer[i + 1];
                    return true;
                }
            }

            return false;
        }

        private float CalculateInterpolationFactor(double renderTime, double olderTime, double newerTime)
        {
            return (float)((renderTime - olderTime) / (newerTime - olderTime));
        }

        private void InterpolateSnapshot(uint projectileId, GameObject projectile, ProjectileSnapshot older, ProjectileSnapshot newer, float t)
        {
            if (!activeTransforms.TryGetValue(projectileId, out var target) || !IsTargetValid(target))
            {
                target = ResolveTarget(projectile);
                if (target.Transform == null)
                    return;

                activeTransforms[projectileId] = target;
            }

            var transform = target.Transform;
            transform.position = Vector3.Lerp(older.Position, newer.Position, t);

            var olderRot = Quaternion.LookRotation(older.Rotation, Vector3.up);
            var newerRot = Quaternion.LookRotation(newer.Rotation, Vector3.up);
            transform.rotation = Quaternion.Slerp(olderRot, newerRot, t);
        }

        private void CleanupOldSnapshots(List<ProjectileSnapshot> buffer, double renderTime)
        {
            int removeCount = 0;
            while (removeCount < buffer.Count - 2 &&
                   buffer[removeCount].Timestamp < renderTime - interpolationDelayMs)
            {
                removeCount++;
            }

            if (removeCount > 0)
            {
                buffer.RemoveRange(0, removeCount);
            }
        }

        public void RegisterProjectile(uint id, GameObject projectile)
        {
            activeProjectiles[id] = projectile;
            activeTransforms.Remove(id);
            try
            {
                var target = ResolveTarget(projectile);
                if (target.Transform != null)
                {
                    activeTransforms[id] = target;
                }
            }
            catch (System.Exception)
            {
                // Moving part not set up yet: resolved at the first interpolation instead, never abort the spawn
            }

            if (!snapshotsBuffers.ContainsKey(id))
            {
                snapshotsBuffers[id] = new List<ProjectileSnapshot>();
            }
        }

        public void UnregisterProjectile(uint id)
        {
            if (activeProjectiles.TryGetValue(id, out var toDel))
            {
                var rocket = toDel.GetComponent<ProjectileRocket>();
                if (rocket != null)
                {
                    rocket.rocket.ProjectileDone();
                }

                DestroyImmediate(toDel);
                activeProjectiles.Remove(id);
            }

            activeTransforms.Remove(id);

            snapshotsBuffers.Remove(id);
        }

        /// <summary>
        /// Interpolated transform plus what it came from: the sword/rocket component and its moving part, to notice a change
        /// </summary>
        private struct ProjectileTarget
        {
            public Transform Transform;
            public Component Owner;
            public UnityEngine.Object Source;
        }

        private static UnityEngine.Object GetMovingPart(Component owner)
        {
            return owner switch
            {
                ProjectileCringeSword cringeSword => cringeSword.movingProjectile,
                ProjectileHeroSword heroSword => heroSword.movingProjectile,
                ProjectileRocket rocket => rocket.rocket,
                _ => null
            };
        }

        private static bool IsTargetValid(ProjectileTarget target)
        {
            if (target.Transform is null || target.Transform.CachedPtr() == System.IntPtr.Zero)
            {
                return false;
            }

            if (target.Owner is null)
            {
                return true;
            }

            var source = GetMovingPart(target.Owner);
            return source is not null && target.Source is not null && source.Ptr() == target.Source.Ptr();
        }

        private ProjectileTarget ResolveTarget(GameObject projectile)
        {
            Component owner = projectile.GetComponent<ProjectileCringeSword>();
            if (owner == null)
            {
                owner = projectile.GetComponent<ProjectileHeroSword>();
            }
            if (owner == null)
            {
                owner = projectile.GetComponent<ProjectileRocket>();
            }

            if (owner == null)
            {
                return new ProjectileTarget { Transform = projectile.transform };
            }

            var source = GetMovingPart(owner);
            if (source == null)
            {
                return default; //Moving part not set yet: try again next frame
            }

            return new ProjectileTarget { Transform = GetProjectileTransform(projectile), Owner = owner, Source = source };
        }

        private Transform GetProjectileTransform(GameObject projectile)
        {
            var projectileCringeSword = projectile.GetComponent<ProjectileCringeSword>();
            if (projectileCringeSword != null)
            {
                return projectileCringeSword.movingProjectile.transform;
            }

            var projectileHeroSword = projectile.GetComponent<ProjectileHeroSword>();
            if (projectileHeroSword != null)
            {
                return projectileHeroSword.movingProjectile.transform;
            }

            var projectileRocket = projectile.GetComponent<ProjectileRocket>();
            if (projectileRocket != null)
            {
                return projectileRocket.rocket.transform;
            }

            return projectile.transform;
        }
    }
}
