using System.Numerics;

namespace MegabonkTogether.Common.Messages
{
    /// <summary>
    /// Client: where to show a host enemy, from the snapshot entries received (no Unity here: it is tested offline).
    /// Every received position becomes a timed sample; the enemy is shown RENDER_DELAY behind:
    ///  - between two close samples (near enemies, sent every tick) it is interpolated linearly, exactly as in 1.0.0;
    ///  - between two far apart samples (mid/far enemies, dead reckoning) each one is extrapolated with its velocity and the
    ///    two are blended, so the path stays smooth and still ends exactly on each sample;
    ///  - past the newest sample it is extrapolated with its velocity (lost packets, sparse sends). When the next sample
    ///    arrives, the shown position becomes a sample itself, so the enemy blends to the new path over RENDER_DELAY: no pop.
    /// Only a real teleport snaps. One code path for every tier: changing tier doesn't jump.
    /// </summary>
    public sealed class EnemyMotionModel
    {
        public const double RENDER_DELAY = 0.1; // 2 enemy ticks
        // Samples closer than this are interpolated linearly (near tier: one per 50 ms tick, a lost one makes 100 ms)
        private const double LINEAR_GAP = 0.16;
        // Further than this from where the enemy was going: a teleport, snap
        private const float TELEPORT_DISTANCE = 6f;
        private const int MAX_SAMPLES = 32;

        private struct Sample
        {
            public double Time;
            public Vector3 Position;
            public Vector3 Velocity;
        }

        private readonly List<Sample> samples = new();

        // Delta baseline: the position of the last send received and its seq. A delta only applies on top of the send just
        // before it; after a lost one the position waits for the next absolute send (the host resends it on the ack).
        private bool hasBaseline;
        private short baseX;
        private short baseY;
        private short baseZ;
        private byte lastSeq;

        public bool HasPosition => samples.Count > 0;

        public void Reset()
        {
            samples.Clear();
            hasBaseline = false;
        }

        /// <summary>
        /// Add a received entry at <paramref name="now"/> (the enemy clock). <paramref name="shownPosition"/>: where the enemy
        /// is shown now, used to start smoothly when nothing was received yet.
        /// </summary>
        public void Apply(in EnemySnapshotEntry entry, double now, in QuantizationFrame frame, Vector3? shownPosition)
        {
            var velocity = entry.HasVelocity
                ? new Vector3(
                    EnemySnapshotCodec.DequantizeVelocity(entry.VX),
                    EnemySnapshotCodec.DequantizeVelocity(entry.VY),
                    EnemySnapshotCodec.DequantizeVelocity(entry.VZ))
                : Vector3.Zero;

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

            var renderTime = now - RENDER_DELAY;
            Vector3 position;
            if (hasPosition)
            {
                position = frame.Dequantize(baseX, baseY, baseZ);
            }
            else if (samples.Count > 0)
            {
                position = Extrapolate(samples[^1], now); //Keep the path, take the new velocity
            }
            else if (shownPosition.HasValue)
            {
                position = shownPosition.Value; //First news lost or before the spawn: from where it stands
            }
            else
            {
                return;
            }

            if (samples.Count == 0)
            {
                if (shownPosition.HasValue && Vector3.DistanceSquared(shownPosition.Value, position) < TELEPORT_DISTANCE * TELEPORT_DISTANCE)
                {
                    samples.Add(new Sample { Time = renderTime, Position = shownPosition.Value });
                }
            }
            else
            {
                var newest = samples[^1];
                if (Vector3.DistanceSquared(Extrapolate(newest, now), position) > TELEPORT_DISTANCE * TELEPORT_DISTANCE)
                {
                    samples.Clear(); //Teleport: shown right away
                }
                else if (renderTime > newest.Time)
                {
                    // Shown past the newest sample (extrapolated): continue from what is shown, not from where it should have been
                    samples.Add(new Sample { Time = renderTime, Position = Evaluate(renderTime), Velocity = newest.Velocity });
                }
            }

            if (samples.Count > 0 && now <= samples[^1].Time)
            {
                samples.RemoveAt(samples.Count - 1); //Same instant (paused clock): the newer one wins
            }

            samples.Add(new Sample { Time = now, Position = position, Velocity = velocity });
            if (samples.Count > MAX_SAMPLES)
            {
                samples.RemoveAt(0);
            }
        }

        /// <summary>
        /// Position to show at <paramref name="now"/> (the enemy clock). False if nothing was received yet.
        /// </summary>
        public bool TryGetPosition(double now, out Vector3 position)
        {
            position = default;
            if (samples.Count == 0)
            {
                return false;
            }

            var renderTime = now - RENDER_DELAY;

            // Drop the samples no longer needed: once the next one is also in the past
            var remove = 0;
            while (remove < samples.Count - 2 && samples[remove + 1].Time <= renderTime)
            {
                remove++;
            }
            if (remove > 0)
            {
                samples.RemoveRange(0, remove);
            }

            position = Evaluate(renderTime);
            return true;
        }

        private Vector3 Evaluate(double renderTime)
        {
            var first = samples[0];
            if (renderTime <= first.Time)
            {
                return first.Position;
            }

            for (var i = 0; i < samples.Count - 1; i++)
            {
                var older = samples[i];
                var newer = samples[i + 1];
                if (renderTime > newer.Time)
                {
                    continue;
                }

                var gap = newer.Time - older.Time;
                var t = gap > 0 ? (float)((renderTime - older.Time) / gap) : 1f;
                if (gap <= LINEAR_GAP)
                {
                    return Vector3.Lerp(older.Position, newer.Position, t);
                }

                // Far apart: each one along its velocity, blended from the older to the newer
                var fromOlder = Extrapolate(older, renderTime);
                var fromNewer = newer.Position + newer.Velocity * (float)(renderTime - newer.Time);
                return Vector3.Lerp(fromOlder, fromNewer, t);
            }

            return Extrapolate(samples[^1], renderTime);
        }

        private static Vector3 Extrapolate(Sample sample, double time)
        {
            var elapsed = Math.Clamp(time - sample.Time, 0, EnemySnapshotPlanner.MAX_EXTRAPOLATION_SECONDS);
            return sample.Position + sample.Velocity * (float)elapsed;
        }
    }
}
