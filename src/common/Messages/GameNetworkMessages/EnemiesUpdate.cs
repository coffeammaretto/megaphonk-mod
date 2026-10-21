using MemoryPack;

namespace MegabonkTogether.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// Host -> clients enemy snapshot, sent unreliable. The enemies are packed by <see cref="EnemySnapshotCodec"/>:
    /// one class per enemy cost ~19 bytes with MemoryPack headers, packed it is 8 (12 when the hp is sent).
    /// A blob always fits in one packet and is decoded on its own.
    /// </summary>
    [MemoryPackable]
    public partial class EnemiesUpdate : IGameNetworkMessage
    {
        /// <summary>
        /// Packet sequence of the enemy snapshot stream, acknowledged by clients (EnemiesAck)
        /// </summary>
        public ushort Seq { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Client -> host, unreliable, ~10 Hz: which EnemiesUpdate packets arrived. The host sends the enemies of a lost one
    /// absolute on the next tick (their delta base is gone on that client).
    /// </summary>
    [MemoryPackable]
    public partial class EnemiesAck : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
        /// <summary>
        /// Newest EnemiesUpdate.Seq received
        /// </summary>
        public ushort Latest { get; set; }
        /// <summary>
        /// Bit i: Latest - i was received (bit 0 is Latest)
        /// </summary>
        public ulong ReceivedMask { get; set; }
    }

    /// <summary>
    /// Sent by each peer when connected: what it can decode. Packets are only compressed for peers that said they can.
    /// </summary>
    [MemoryPackable]
    public partial class PeerCapabilities : IGameNetworkMessage
    {
        public uint OwnerId { get; set; }
        public bool SupportsCompression { get; set; }
    }
}
