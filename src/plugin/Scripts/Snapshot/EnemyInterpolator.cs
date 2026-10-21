using Assets.Scripts.Actors.Enemies;
using MegabonkTogether.Common.Messages;
using MegabonkTogether.Helpers;
using UnityEngine;

namespace MegabonkTogether.Scripts.Snapshot
{
    /// <summary>
    /// Client: shows a host enemy from the snapshots received, 0.1 s behind (EnemyMotionModel): near enemies come every tick
    /// and are interpolated between exact samples, mid/far ones come when they leave their predicted path and are
    /// extrapolated with their velocity in between. A teleport snaps.
    /// </summary>
    //TODO: Find a way to make abstract class work with IL2CPP because its not working for some reason ¯\_(ツ)_/¯
    public class EnemyInterpolator : MonoBehaviour
    {
        private Enemy enemy;
        private Transform enemyTransform;

        private const float ROTATION_TIME_CONSTANT = 0.08f;

        private readonly EnemyMotionModel motion = new EnemyMotionModel();
        private bool hasRotation;
        private Quaternion rotation = Quaternion.identity;
        private Quaternion targetRotation = Quaternion.identity;

        // The hp is only sent when it changed: the last known one
        private bool hasLastHp;
        private float lastHp;

        protected void Update()
        {
            if (hasLastHp)
            {
                enemy.hp = lastHp;
            }

            var transform = enemyTransform;
            if (transform is null || transform.CachedPtr() == System.IntPtr.Zero)
            {
                return;
            }

            // EnemyClock stops while the game is paused (party pause pauses everyone): nothing moves past the pause
            if (!motion.TryGetPosition(NetworkHandler.EnemyClock, out var position))
            {
                return;
            }

            rotation = Quaternion.Slerp(rotation, targetRotation, 1f - Mathf.Exp(-NetworkHandler.FrameDeltaTime / ROTATION_TIME_CONSTANT));
            transform.SetPositionAndRotation(new Vector3(position.X, position.Y, position.Z), rotation);
        }

        /// <summary>
        /// Called on every spawn: pooled enemies keep their interpolator, so the previous life's state is dropped
        /// </summary>
        public void Initialize(Enemy enemy)
        {
            this.enemy = enemy;
            enemyTransform = enemy.transform;
            motion.Reset();
            hasRotation = false;
            hasLastHp = false;
            lastHp = 0f;
        }

        public void ApplySnapshot(EnemySnapshotEntry entry, double now)
        {
            if (entry.HasHp)
            {
                hasLastHp = true;
                lastHp = entry.Hp;
            }

            targetRotation = Quaternion.Euler(0, Quantizer.DequantizeYawByte(entry.Yaw), 0);
            if (!hasRotation)
            {
                hasRotation = true;
                rotation = targetRotation;
            }

            System.Numerics.Vector3? shown = null;
            var transform = enemyTransform;
            if (!motion.HasPosition && transform is not null && transform.CachedPtr() != System.IntPtr.Zero)
            {
                var current = transform.position; //Only read before the first sample: start from where it spawned
                shown = new System.Numerics.Vector3(current.x, current.y, current.z);
            }

            motion.Apply(entry, now, Quantizer.GetFrame(), shown);
        }
    }
}
