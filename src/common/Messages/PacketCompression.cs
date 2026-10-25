using System.IO.Compression;

namespace Megaphonk.Common.Messages
{
    /// <summary>
    /// Compression of whole packets (a message or a MessageBatch, as serialized). A compressed packet is
    ///   COMPRESSED_HEADER, varint(raw length), Brotli data
    /// Any other first byte is an uncompressed packet: the union tag of its message, decoded as before. COMPRESSED_HEADER is
    /// never used as a union tag, so a build without compression drops these packets as undecodable instead of misreading them.
    /// Brotli quality 4, window 16: measured on enemy snapshots, pickup/death bursts and EnemyDamaged batches it compresses
    /// better than Deflate (fastest) for the same CPU (~30 us per 1 KB packet), decompression ~5 us.
    /// </summary>
    public static class PacketCompression
    {
        public const byte COMPRESSED_HEADER = 249; // Reserved: never a MemoryPackUnion tag of IGameNetworkMessage
        public const int MIN_COMPRESSED_SIZE = 200; // Smaller packets gain too little
        public const int MAX_RAW_SIZE = 64 * 1024;

        private const int QUALITY = 4;
        private const int WINDOW = 16;

        public static bool IsCompressed(ReadOnlySpan<byte> packet)
        {
            return packet.Length > 0 && packet[0] == COMPRESSED_HEADER;
        }

        /// <summary>
        /// The compressed packet, or null when it would not be smaller (send the raw one)
        /// </summary>
        public static byte[]? Compress(byte[] raw)
        {
            if (raw.Length < MIN_COMPRESSED_SIZE || raw.Length > MAX_RAW_SIZE)
            {
                return null;
            }

            var buffer = new byte[raw.Length];
            buffer[0] = COMPRESSED_HEADER;
            var offset = 1;
            var length = (uint)raw.Length;
            while (length >= 0x80)
            {
                buffer[offset++] = (byte)(length | 0x80);
                length >>= 7;
            }
            buffer[offset++] = (byte)length;

            if (!BrotliEncoder.TryCompress(raw, buffer.AsSpan(offset), out var written, QUALITY, WINDOW))
            {
                return null; //Doesn't fit in fewer bytes than the raw packet
            }

            var size = offset + written;
            return size < raw.Length ? buffer.AsSpan(0, size).ToArray() : null;
        }

        /// <summary>
        /// Decompress a packet starting with COMPRESSED_HEADER. False if it is corrupt.
        /// </summary>
        public static bool TryDecompress(ReadOnlySpan<byte> packet, out byte[] raw)
        {
            raw = Array.Empty<byte>();
            if (!IsCompressed(packet))
            {
                return false;
            }

            var offset = 1;
            uint length = 0;
            for (var shift = 0; ; shift += 7)
            {
                if (offset >= packet.Length || shift > 28)
                {
                    return false;
                }

                var b = packet[offset++];
                length |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            if (length == 0 || length > MAX_RAW_SIZE)
            {
                return false;
            }

            raw = new byte[length];
            return BrotliDecoder.TryDecompress(packet.Slice(offset), raw, out var written) && written == length;
        }

        /// <summary>
        /// Round trip once: false if Brotli is not usable in this runtime (then packets are sent uncompressed)
        /// </summary>
        public static bool SelfTest()
        {
            try
            {
                var sample = new byte[512];
                for (var i = 0; i < sample.Length; i++)
                {
                    sample[i] = (byte)(i % 7);
                }

                var compressed = Compress(sample);
                return compressed != null && TryDecompress(compressed, out var raw) && raw.AsSpan().SequenceEqual(sample);
            }
            catch
            {
                return false;
            }
        }
    }
}
