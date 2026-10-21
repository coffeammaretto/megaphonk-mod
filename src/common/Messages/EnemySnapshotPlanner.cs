using System.Numerics;

namespace MegabonkTogether.Common.Messages
{
    /// <summary>
    /// Position quantization of the current map (same arithmetic as the plugin's Quantizer)
    /// </summary>
    public readonly struct QuantizationFrame
    {
        public readonly float Min;
        public readonly float Range;

        public QuantizationFrame(float min, float range)
        {
            Min = min;
            Range = range;
        }

        public short Quantize(float value)
        {
            float t = (value - Min) / Range;
            return (short)(t * short.MaxValue);
        }

        public float Dequantize(short q)
        {
            float t = q / (float)short.MaxValue;
            return Min + t * Range;
        }

        public Vector3 Dequantize(short x, short y, short z)
        {
            return new Vector3(Dequantize(x), Dequantize(y), Dequantize(z));
        }
    }

    /// <summary>
    /// Host side snapshot state of one enemy: what the clients were last sent for it, so what they are predicting
    /// </summary>
    public class EnemySendState
    {
        public bool HasSent;
        // Last sent position (quantized): what the client shows, and the base of the next delta
        public short X;
        public short Y;
        public short Z;
        public byte Yaw;
        // Last sent velocity (dequantized), the client extrapolates with it from SentAt (game time)
        public Vector3 Velocity;
        public double SentAt;
        public float Hp;
        public byte Seq;
        // Real time of the last keyframe (absolute position, velocity and hp)
        public float KeyframeAt;
        // Next send absolute (and with the hp): a client lost a packet with this enemy (ack), so its delta base is gone
        public bool ForceAbsolute;
        // Real time of a second absolute send after a teleport or a move too big for a delta (0: none)
        public float FollowUpAt;

        // Last read, for the velocity estimate
        public bool HasRead;
        public Vector3 ReadPosition;
        public double ReadAt;
        // Smoothed: positions move in physics steps, a raw difference between two reads jitters
        public Vector3 EstimatedVelocity;

        // Send every Period ticks (1, 2 or 4), from the distance to the nearest remote player when last read
        public uint Period = 1;
        public long LastTick;
    }

    /// <summary>
    /// Which enemies to send, and how (dead reckoning + deltas). Clients extrapolate each enemy from its last position and
    /// velocity: the host keeps that same prediction and only sends when the real enemy drifts away from it, its velocity or
    /// yaw changes, its hp changes, or its keyframe (every second, absolute: the loss recovery) is due.
    /// Distance tiers cap how often a far enemy can be sent. No Unity here: it is tested offline.
    /// </summary>
    public static class EnemySnapshotPlanner
    {
        public const float KEYFRAME_INTERVAL_SECONDS = 1f;
        // Clients stop extrapolating after this long without news (lost packets): the keyframe comes before
        public const float MAX_EXTRAPOLATION_SECONDS = 1.25f;

        // Distance tiers (enemy tick is 20 Hz): near every tick, mid every 2nd tick (10 Hz), far every 4th (5 Hz).
        // With hysteresis, so an enemy on a boundary doesn't change rate every tick: near up to 27 m (back from 23 m),
        // far beyond 65 m (back from 55 m)
        private const float NEAR_ENTER_DISTANCE = 23f;
        private const float NEAR_LEAVE_DISTANCE = 27f;
        private const float FAR_ENTER_DISTANCE = 65f;
        private const float FAR_LEAVE_DISTANCE = 55f;
        public const uint MID_PERIOD = 2;
        public const uint FAR_PERIOD = 4;
        // An hp change is still sent at 10 Hz or more
        private const uint HP_PERIOD = 2;

        // Prediction error allowed before a send (m): near, mid, far
        private const float NEAR_PREDICTION_ERROR = 0.5f;
        private const float MID_PREDICTION_ERROR = 0.75f;
        private const float FAR_PREDICTION_ERROR = 1f;
        // Velocity change (m/s) that is sent right away: stops, starts, knockback
        private const float VELOCITY_CHANGE = 1f;
        // Below this the enemy is still: no velocity sent
        private const float STILL_SPEED = 0.2f;
        // Faster than this between two reads is a teleport, not a velocity
        private const float TELEPORT_SPEED = 40f;
        private const float VELOCITY_SMOOTHING = 0.5f;
        // Moved this far while paused: a teleport
        private const float TELEPORT_DISTANCE = 3f;
        private const float FOLLOW_UP_SECONDS = 0.12f;
        // Keyframes are spread over the second by id (20 ticks), else enemies spawned together keyframe in the same tick
        private const int KEYFRAME_PHASES = 20;
        private const float YAW_TRESHOLD = 10f;
        private const float HP_TRESHOLD = 1f;

        /// <summary>
        /// Whether the enemy must be looked at this tick (its hp at least). keyframe: send it in full now.
        /// positionDue: its tier allows a send this tick.
        /// </summary>
        public static bool IsReadDue(EnemySendState state, long tick, uint id, float realTime, out bool keyframe, out bool absolute, out bool positionDue)
        {
            keyframe = !state.HasSent || realTime - state.KeyframeAt >= KEYFRAME_INTERVAL_SECONDS;
            absolute = state.ForceAbsolute || (state.FollowUpAt > 0f && realTime >= state.FollowUpAt);
            positionDue = keyframe || absolute || IsDue(tick, id, state.Period);
            return positionDue || IsDue(tick, id, Math.Min(state.Period, HP_PERIOD));
        }

        public static bool HasHpChanged(EnemySendState state, float hp)
        {
            return !state.HasSent || MathF.Abs(state.Hp - hp) > HP_TRESHOLD;
        }

        /// <summary>
        /// Staggered by id so the enemies sent every 2nd/4th tick are spread over the ticks
        /// </summary>
        public static bool IsDue(long tick, uint id, uint period)
        {
            return (((uint)tick + id) & (period - 1)) == 0; //Periods are powers of 2
        }

        /// <summary>
        /// Called with a fresh read of the enemy (when IsReadDue and its position is due or its hp changed). Updates the
        /// velocity estimate and the tier, and returns the entry to send if one is needed (the state then records it).
        /// gameTime: the enemy clock (stops while the game is paused, as on the clients). paused: the enemies don't move
        /// (pause, hit stop): their velocity is zero, so the clients are told to stop extrapolating.
        /// </summary>
        public static bool TryPlan(EnemySendState state, uint id, Vector3 position, byte yaw, float hp, bool keyframe, bool absolute,
            bool hpChanged, bool paused, double gameTime, float realTime, in QuantizationFrame frame, List<Vector3> referencePositions,
            out EnemySnapshotEntry entry)
        {
            entry = default;

            var readInterval = gameTime - state.ReadAt;
            var teleported = false;
            if (!state.HasRead || paused)
            {
                state.EstimatedVelocity = Vector3.Zero;
                teleported = state.HasRead && (position - state.ReadPosition).LengthSquared() > TELEPORT_DISTANCE * TELEPORT_DISTANCE;
            }
            else if (readInterval > 0.001) //Else no game time passed: keep the estimate
            {
                var measured = (position - state.ReadPosition) / (float)readInterval;
                teleported = measured.LengthSquared() > TELEPORT_SPEED * TELEPORT_SPEED;
                state.EstimatedVelocity = teleported
                    ? Vector3.Zero
                    : Vector3.Lerp(state.EstimatedVelocity, measured, VELOCITY_SMOOTHING);
            }
            var velocity = state.EstimatedVelocity;
            state.HasRead = true;
            state.ReadPosition = position;
            state.ReadAt = gameTime;

            state.Period = GetPeriod(position, referencePositions, state.Period);

            var still = velocity.LengthSquared() < STILL_SPEED * STILL_SPEED;
            var vx = still ? (sbyte)0 : EnemySnapshotCodec.QuantizeVelocity(velocity.X);
            var vy = still ? (sbyte)0 : EnemySnapshotCodec.QuantizeVelocity(velocity.Y);
            var vz = still ? (sbyte)0 : EnemySnapshotCodec.QuantizeVelocity(velocity.Z);
            var sentVelocity = new Vector3(
                EnemySnapshotCodec.DequantizeVelocity(vx),
                EnemySnapshotCodec.DequantizeVelocity(vy),
                EnemySnapshotCodec.DequantizeVelocity(vz));

            var send = keyframe || absolute || teleported || hpChanged;
            if (!send)
            {
                var elapsed = (float)Math.Min(gameTime - state.SentAt, MAX_EXTRAPOLATION_SECONDS);
                var predicted = frame.Dequantize(state.X, state.Y, state.Z) + state.Velocity * elapsed;
                var maxError = state.Period switch
                {
                    1 => NEAR_PREDICTION_ERROR,
                    MID_PERIOD => MID_PREDICTION_ERROR,
                    _ => FAR_PREDICTION_ERROR
                };

                var yawSteps = (byte)(yaw - state.Yaw);
                var yawDelta = Math.Min(yawSteps, 256 - yawSteps) * (360f / 256f);

                send = (position - predicted).LengthSquared() > maxError * maxError
                    || (sentVelocity - state.Velocity).LengthSquared() > VELOCITY_CHANGE * VELOCITY_CHANGE
                    || yawDelta > YAW_TRESHOLD;
            }

            if (!send)
            {
                return false;
            }

            var x = frame.Quantize(position.X);
            var y = frame.Quantize(position.Y);
            var z = frame.Quantize(position.Z);
            int dx = x - state.X;
            int dy = y - state.Y;
            int dz = z - state.Z;
            // Absolute on keyframes and forced sends (loss recovery), teleports, and when the move doesn't fit a byte per axis
            var isDelta = !keyframe && !absolute && !teleported && state.HasSent
                && dx >= sbyte.MinValue && dx <= sbyte.MaxValue
                && dy >= sbyte.MinValue && dy <= sbyte.MaxValue
                && dz >= sbyte.MinValue && dz <= sbyte.MaxValue;
            var sendHp = keyframe || absolute || hpChanged;
            var seq = (byte)((state.Seq + 1) & EnemySnapshotCodec.SEQ_MASK);

            entry = new EnemySnapshotEntry
            {
                Id = id,
                Seq = seq,
                IsDelta = isDelta,
                X = isDelta ? (short)dx : x,
                Y = isDelta ? (short)dy : y,
                Z = isDelta ? (short)dz : z,
                Yaw = yaw,
                HasVelocity = !still,
                VX = vx,
                VY = vy,
                VZ = vz,
                HasHp = sendHp,
                Hp = hp,
            };

            state.X = x;
            state.Y = y;
            state.Z = z;
            state.Yaw = yaw;
            state.Velocity = sentVelocity;
            state.SentAt = gameTime;
            state.Seq = seq;
            if (sendHp)
            {
                state.Hp = hp;
            }
            state.ForceAbsolute = false;
            if (absolute)
            {
                state.FollowUpAt = 0f;
            }
            else if (!isDelta && !keyframe)
            {
                // Teleport or big move: that one absolute send may be lost, a second one soon after
                state.FollowUpAt = realTime + FOLLOW_UP_SECONDS;
            }
            if (keyframe)
            {
                state.KeyframeAt = NextKeyframeAt(state, id, realTime);
            }
            state.HasSent = true;

            return true;
        }

        /// <summary>
        /// The first keyframe is right away (first sight), the next one after a part of the interval depending on the id,
        /// then every interval keeping that phase
        /// </summary>
        private static float NextKeyframeAt(EnemySendState state, uint id, float realTime)
        {
            if (!state.HasSent)
            {
                return realTime - (id % KEYFRAME_PHASES) * (KEYFRAME_INTERVAL_SECONDS / KEYFRAME_PHASES);
            }

            var next = state.KeyframeAt + KEYFRAME_INTERVAL_SECONDS;
            return realTime - next >= KEYFRAME_INTERVAL_SECONDS ? realTime : next;
        }

        public static uint GetPeriod(Vector3 position, List<Vector3> referencePositions, uint currentPeriod)
        {
            if (referencePositions.Count == 0)
            {
                return 1;
            }

            var minSqrDistance = float.MaxValue;
            foreach (var reference in referencePositions)
            {
                var sqrDistance = (position - reference).LengthSquared();
                if (sqrDistance < minSqrDistance)
                {
                    minSqrDistance = sqrDistance;
                }
            }

            var nearDistance = currentPeriod == 1 ? NEAR_LEAVE_DISTANCE : NEAR_ENTER_DISTANCE;
            if (minSqrDistance < nearDistance * nearDistance)
            {
                return 1;
            }

            var farDistance = currentPeriod == FAR_PERIOD ? FAR_LEAVE_DISTANCE : FAR_ENTER_DISTANCE;
            return minSqrDistance < farDistance * farDistance ? MID_PERIOD : FAR_PERIOD;
        }
    }
}
