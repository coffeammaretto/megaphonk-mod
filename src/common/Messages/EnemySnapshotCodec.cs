using System.Buffers.Binary;

namespace MegabonkTogether.Common.Messages
{
    /// <summary>
    /// One enemy of an enemy snapshot. Position and yaw are already quantized (Quantizer on the plugin side).
    /// </summary>
    public struct EnemySnapshotEntry
    {
        public uint Id;
        /// <summary>
        /// Per enemy send counter (5 bits): a delta only applies on top of the send just before it
        /// </summary>
        public byte Seq;
        /// <summary>
        /// X, Y, Z are relative to the position of this enemy's previous send (each in -128..127), else absolute
        /// </summary>
        public bool IsDelta;
        public short X;
        public short Y;
        public short Z;
        /// <summary>
        /// 256 steps over 360 degrees
        /// </summary>
        public byte Yaw;
        /// <summary>
        /// Velocity for dead reckoning, VELOCITY_STEP m/s per unit. Not sent (zero) when the enemy is still.
        /// </summary>
        public bool HasVelocity;
        public sbyte VX;
        public sbyte VY;
        public sbyte VZ;
        /// <summary>
        /// The hp is only sent when it changed (and on first sight and keyframes): receivers keep the last one otherwise
        /// </summary>
        public bool HasHp;
        public float Hp;
    }

    /// <summary>
    /// Compact encoding of enemy snapshots (<see cref="GameNetworkMessages.EnemiesUpdate"/>).
    /// Entries are sorted by id, each one is:
    ///   varint(id - previous id)       previous id is 0 at the start of every blob
    ///   flags                          bit 0 hp, bit 1 delta, bit 2 velocity, bits 3-7 seq
    ///   position                       delta: 3 x int8, absolute: 3 x int16 little endian (quantized)
    ///   yaw                            1 byte
    ///   velocity                       3 x int8 (VELOCITY_STEP m/s), only with the velocity flag
    ///   hp                             float32 little endian, only with the hp flag
    /// Typical: 9 bytes for a moving enemy sent as a delta, 12 absolute, +4 with the hp.
    /// </summary>
    public static class EnemySnapshotCodec
    {
        public const float VELOCITY_STEP = 0.25f; // int8: +-31.75 m/s
        public const int SEQ_MASK = 0x1F;

        private const byte FLAG_HP = 1;
        private const byte FLAG_DELTA = 2;
        private const byte FLAG_VELOCITY = 4;

        // varint of a 32 bit value (5) + flags (1) + position (6) + yaw (1) + velocity (3) + hp (4)
        public const int MAX_ENTRY_BYTES = 20;

        /// <summary>
        /// Encode the entries (sorted in place by id) into blobs of at most <paramref name="maxBlobBytes"/> bytes.
        /// Each blob decodes on its own, so a lost packet only loses its own enemies.
        /// <paramref name="blobEntryCounts"/>, if given, gets the number of entries of each blob (consecutive in the sorted list).
        /// </summary>
        public static List<byte[]> Encode(List<EnemySnapshotEntry> entries, int maxBlobBytes, List<int>? blobEntryCounts = null)
        {
            var blobs = new List<byte[]>();
            if (entries.Count == 0)
            {
                return blobs;
            }

            entries.Sort(static (a, b) => a.Id.CompareTo(b.Id));

            var buffer = new byte[Math.Max(maxBlobBytes, MAX_ENTRY_BYTES)];
            var length = 0;
            var count = 0;
            uint previousId = 0;

            foreach (var entry in entries)
            {
                if (length > 0 && length + MAX_ENTRY_BYTES > buffer.Length)
                {
                    blobs.Add(buffer.AsSpan(0, length).ToArray());
                    blobEntryCounts?.Add(count);
                    length = 0;
                    count = 0;
                    previousId = 0;
                }
                count++;

                length = WriteVarint(buffer, length, entry.Id - previousId);

                var flags = (byte)((entry.Seq & SEQ_MASK) << 3);
                if (entry.HasHp)
                {
                    flags |= FLAG_HP;
                }
                if (entry.IsDelta)
                {
                    flags |= FLAG_DELTA;
                }
                if (entry.HasVelocity)
                {
                    flags |= FLAG_VELOCITY;
                }
                buffer[length++] = flags;

                if (entry.IsDelta)
                {
                    buffer[length++] = (byte)(sbyte)entry.X;
                    buffer[length++] = (byte)(sbyte)entry.Y;
                    buffer[length++] = (byte)(sbyte)entry.Z;
                }
                else
                {
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(length), entry.X);
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(length + 2), entry.Y);
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(length + 4), entry.Z);
                    length += 6;
                }

                buffer[length++] = entry.Yaw;

                if (entry.HasVelocity)
                {
                    buffer[length++] = (byte)entry.VX;
                    buffer[length++] = (byte)entry.VY;
                    buffer[length++] = (byte)entry.VZ;
                }

                if (entry.HasHp)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(length), BitConverter.SingleToInt32Bits(entry.Hp));
                    length += 4;
                }

                previousId = entry.Id;
            }

            if (length > 0)
            {
                blobs.Add(buffer.AsSpan(0, length).ToArray());
                blobEntryCounts?.Add(count);
            }

            return blobs;
        }

        /// <summary>
        /// Decode a blob into <paramref name="result"/> (not cleared). A truncated or corrupt blob stops at the last whole entry.
        /// </summary>
        public static void Decode(byte[] data, List<EnemySnapshotEntry> result)
        {
            if (data == null)
            {
                return;
            }

            var offset = 0;
            uint previousId = 0;

            while (offset < data.Length)
            {
                if (!TryReadVarint(data, ref offset, out var idDelta) || offset >= data.Length)
                {
                    return;
                }

                var flags = data[offset++];
                var entry = new EnemySnapshotEntry
                {
                    Id = previousId + (uint)idDelta,
                    Seq = (byte)(flags >> 3),
                    HasHp = (flags & FLAG_HP) != 0,
                    IsDelta = (flags & FLAG_DELTA) != 0,
                    HasVelocity = (flags & FLAG_VELOCITY) != 0,
                };

                var size = (entry.IsDelta ? 3 : 6) + 1 + (entry.HasVelocity ? 3 : 0) + (entry.HasHp ? 4 : 0);
                if (offset + size > data.Length)
                {
                    return;
                }

                if (entry.IsDelta)
                {
                    entry.X = (sbyte)data[offset];
                    entry.Y = (sbyte)data[offset + 1];
                    entry.Z = (sbyte)data[offset + 2];
                    offset += 3;
                }
                else
                {
                    entry.X = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset));
                    entry.Y = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 2));
                    entry.Z = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 4));
                    offset += 6;
                }

                entry.Yaw = data[offset++];

                if (entry.HasVelocity)
                {
                    entry.VX = (sbyte)data[offset];
                    entry.VY = (sbyte)data[offset + 1];
                    entry.VZ = (sbyte)data[offset + 2];
                    offset += 3;
                }

                if (entry.HasHp)
                {
                    entry.Hp = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)));
                    offset += 4;
                }

                result.Add(entry);
                previousId = entry.Id;
            }
        }

        public static sbyte QuantizeVelocity(float metersPerSecond)
        {
            return (sbyte)Math.Clamp((int)MathF.Round(metersPerSecond / VELOCITY_STEP), sbyte.MinValue, sbyte.MaxValue);
        }

        public static float DequantizeVelocity(sbyte value)
        {
            return value * VELOCITY_STEP;
        }

        private static int WriteVarint(byte[] buffer, int offset, ulong value)
        {
            while (value >= 0x80)
            {
                buffer[offset++] = (byte)(value | 0x80);
                value >>= 7;
            }

            buffer[offset++] = (byte)value;
            return offset;
        }

        private static bool TryReadVarint(byte[] data, ref int offset, out ulong value)
        {
            value = 0;
            for (var shift = 0; shift < 64 && offset < data.Length; shift += 7)
            {
                var b = data[offset++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
