using Assets.Scripts.Actors.Enemies;
using MegabonkTogether.Common.Messages;
using MegabonkTogether.Helpers;
using UnityEngine;

namespace MegabonkTogether.Scripts.Snapshot
{
    /// <summary>
    /// Client: shows a host enemy by dead reckoning. The host sends a position and a velocity only when the enemy drifts from
    /// that prediction (and a keyframe every second): the enemy moves along the last one, and a correction fades the
    /// difference out when a new one arrives (a teleport snaps). No interpolation delay.
    /// </summary>
    //TODO: Find a way to make abstract class work with IL2CPP because its not working for some reason ¯\_(ツ)_/¯
    public class EnemyInterpolator : MonoBehaviour
    {
        private Enemy enemy;
        private Transform enemyTransform;

        // A correction further than this is a teleport: snap
        private const float SNAP_DISTANCE = 3f;
        // Time constants of the exponential fade: ~95% of a correction gone after 120 ms
        private const float CORRECTION_TIME_CONSTANT = 0.04f;
        private const float ROTATION_TIME_CONSTANT = 0.05f;

        // The enemy is at origin + velocity * (time - originTime) (at most MAX_EXTRAPOLATION_SECONDS), plus the correction
        private bool hasState;
        private Vector3 origin;
        private Vector3 velocity;
        private double originTime;
        private Vector3 correction;
        private Quaternion rotation = Quaternion.identity;
        private Quaternion targetRotation = Quaternion.identity;

        // Delta baseline: the position of the last send received, and its seq. A delta only applies on top of the send just
        // before it; after a lost one the position waits for the next absolute send (keyframe at the latest).
        private bool hasBaseline;
        private short baseX;
        private short baseY;
        private short baseZ;
        private byte lastSeq;

        // The hp is only sent when it changed: the last known one
        private bool hasLastHp;
        private float lastHp;

        protected void Update()
        {
            if (!hasState)
                return;

            if (hasLastHp)
            {
                enemy.hp = lastHp;
            }

            var transform = enemyTransform;
            if (transform is null || transform.CachedPtr() == System.IntPtr.Zero)
            {
                return;
            }

            var deltaTime = NetworkHandler.FrameDeltaTime;
            correction *= Mathf.Exp(-deltaTime / CORRECTION_TIME_CONSTANT);
            rotation = Quaternion.Slerp(rotation, targetRotation, 1f - Mathf.Exp(-deltaTime / ROTATION_TIME_CONSTANT));

            // EnemyClock stops while the game is paused (party pause pauses everyone): no extrapolation past the pause
            transform.SetPositionAndRotation(GetPredictedPosition(NetworkHandler.EnemyClock) + correction, rotation);
        }

        /// <summary>
        /// Called on every spawn: pooled enemies keep their interpolator, so the previous life's state is dropped
        /// </summary>
        public void Initialize(Enemy enemy)
        {
            this.enemy = enemy;
            enemyTransform = enemy.transform;
            hasState = false;
            correction = Vector3.zero;
            velocity = Vector3.zero;
            hasBaseline = false;
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
            var newVelocity = entry.HasVelocity
                ? new Vector3(
                    EnemySnapshotCodec.DequantizeVelocity(entry.VX),
                    EnemySnapshotCodec.DequantizeVelocity(entry.VY),
                    EnemySnapshotCodec.DequantizeVelocity(entry.VZ))
                : Vector3.zero;

            bool hasPosition;
            if (!entry.IsDelta)
            {
                baseX = entry.X;
                baseY = entry.Y;
                baseZ = entry.Z;
                hasBaseline = true;
                hasPosition = true;
            }
            else if (hasBaseline && entry.Seq == ((lastSeq + 1) & EnemySnapshotCodec.SEQ_MASK))
            {
                baseX = (short)(baseX + entry.X);
                baseY = (short)(baseY + entry.Y);
                baseZ = (short)(baseZ + entry.Z);
                hasPosition = true;
            }
            else
            {
                hasBaseline = false; //A send was lost: its delta base is unknown until the next absolute one
                hasPosition = false;
            }
            lastSeq = entry.Seq;

            if (hasPosition)
            {
                var position = new Vector3(Quantizer.Dequantize(baseX), Quantizer.Dequantize(baseY), Quantizer.Dequantize(baseZ));
                Vector3 shown;
                if (hasState)
                {
                    shown = GetPredictedPosition(now) + correction;
                }
                else
                {
                    // First news of this enemy: start from where it spawned, and its yaw at once
                    var transform = enemyTransform;
                    shown = transform is not null && transform.CachedPtr() != System.IntPtr.Zero ? transform.position : position;
                    rotation = targetRotation;
                }

                correction = shown - position;
                if (correction.sqrMagnitude > SNAP_DISTANCE * SNAP_DISTANCE)
                {
                    correction = Vector3.zero;
                }
                origin = position;
            }
            else if (hasState)
            {
                // Velocity, yaw and hp still apply: keep extrapolating from where it is predicted now
                origin = GetPredictedPosition(now);
            }
            else
            {
                // Its first send came before the spawn (or was lost): move it from where it is until the next absolute one
                var transform = enemyTransform;
                if (transform is null || transform.CachedPtr() == System.IntPtr.Zero)
                {
                    return;
                }
                origin = transform.position;
                rotation = targetRotation;
                correction = Vector3.zero;
            }

            velocity = newVelocity;
            originTime = now;
            hasState = true;
        }

        private Vector3 GetPredictedPosition(double now)
        {
            var elapsed = (float)System.Math.Min(now - originTime, EnemySnapshotPlanner.MAX_EXTRAPOLATION_SECONDS);
            return origin + velocity * Mathf.Max(0f, elapsed);
        }
    }
}
