using System.Numerics;

namespace Megaphonk.Common.Messages
{
    /// <summary>
    /// Client: where to show a host enemy, from the snapshot entries received (no Unity here: it is tested offline).
    /// Every received position becomes a timed sample; the enemy is shown RENDER_DELAY behind:
    ///  - near enemies (sent every tick, samples close together): linear interpolation between samples, and past the newest
    ///    one it holds there, exactly as 1.0.0. No extrapolation: a late packet can't carry an enemy past where it really is;
    ///  - mid/far enemies (sparse sends, dead reckoning on the host): between two samples each one is extrapolated with its
    ///    velocity and the two are blended; past the newest one it is extrapolated, as the host predicts.
    /// When a sample arrives while the enemy is shown past the newest one (held or extrapolated), what is shown becomes a sample,
    /// so the enemy continues from there over RENDER_DELAY instead of jumping. Only a real teleport snaps.
    /// </summary>
    public sealed class EnemyMotionModel
    {
        public const double RENDER_DELAY = 0.1; // 4 enemy ticks at 40 Hz
        // A sample arriving this soon after the previous one is a near enemy sample (one per 25 ms tick, a few lost still count).
        // Near samples are always absolute: a delta this soon is a mid enemy sent every 4th tick, extrapolated like the host predicts
        private const double LINEAR_GAP = 0.16;
        // Further than this from where the enemy was going: a teleport, snap
        private const float TELEPORT_DISTANCE = 6f;
        // Plus this speed times the time since the newest sample (strong knockback between two near samples)
        private const float TELEPORT_SPEED = 60f;
        private const int MAX_SAMPLES = 32;

        private struct Sample
        {
            public double Time;
            public Vector3 Position;
            public Vector3 Velocity;
            // Near sample: hold at it past it, never extrapolate
            public bool Hold;
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
            // samples[^1] is always a received sample here (shown position samples go before the new one)
            var near = !entry.IsDelta && samples.Count > 0 && now - samples[^1].Time <= LINEAR_GAP;

            Vector3 position;
            if (hasPosition)
            {
                position = frame.Dequantize(baseX, baseY, baseZ);
            }
            else if (samples.Count > 0)
            {
                position = Project(samples[^1], now); //Position unknown: keep the path (held when near)
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
                    samples.Add(new Sample { Time = renderTime, Position = shownPosition.Value, Hold = true });
                }
            }
            else
            {
                var newest = samples[^1];
                var expected = Project(newest, now);
                var limit = TELEPORT_DISTANCE + TELEPORT_SPEED * (float)Math.Min(now - newest.Time, LINEAR_GAP);
                if (Vector3.DistanceSquared(expected, position) > limit * limit)
                {
                    samples.Clear(); //Teleport: shown right away
                }
                else if (renderTime > newest.Time)
                {
                    // Shown past the newest sample (held or extrapolated): continue from what is shown
                    samples.Add(new Sample { Time = renderTime, Position = Evaluate(renderTime), Velocity = newest.Velocity, Hold = newest.Hold });
                }
            }

            if (samples.Count > 0 && now <= samples[^1].Time)
            {
                samples.RemoveAt(samples.Count - 1); //Same instant (paused clock): the newer one wins
            }

            samples.Add(new Sample { Time = now, Position = position, Velocity = velocity, Hold = near });
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
                if (gap <= LINEAR_GAP || newer.Hold)
                {
                    return Vector3.Lerp(older.Position, newer.Position, t);
                }

                // Far apart sparse samples: each one along its velocity, blended from the older to the newer
                var fromOlder = Project(older, renderTime);
                var fromNewer = newer.Position + newer.Velocity * (float)(renderTime - newer.Time);
                return Vector3.Lerp(fromOlder, fromNewer, t);
            }

            return Project(samples[^1], renderTime);
        }

        /// <summary>
        /// Where a sample says the enemy is at <paramref name="time"/> (after it): held for a near sample, extrapolated else
        /// </summary>
        private static Vector3 Project(Sample sample, double time)
        {
            if (sample.Hold)
            {
                return sample.Position;
            }

            var elapsed = Math.Clamp(time - sample.Time, 0, EnemySnapshotPlanner.MAX_EXTRAPOLATION_SECONDS);
            return sample.Position + sample.Velocity * (float)elapsed;
        }
    }
}
