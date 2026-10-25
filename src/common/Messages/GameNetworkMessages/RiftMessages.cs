using MemoryPack;
using System.Numerics;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    public enum RiftType : byte
    {
        /// <summary>
        /// Zones to hold: each fills while a player stands in it
        /// </summary>
        HoldZones = 0
    }

    /// <summary>
    /// The host opened a rift (an ascension objective with a time limit)
    /// </summary>
    [MemoryPackable]
    public partial class RiftStarted : IGameNetworkMessage
    {
        public int Id { get; set; }
        public RiftType Type { get; set; }
        public Vector3[] Zones { get; set; } = System.Array.Empty<Vector3>();
        public float ZoneRadius { get; set; }
        public float Duration { get; set; }
    }

    /// <summary>
    /// Host: the rift's progress (each zone from 0 to 1) and time left, a few times per second
    /// </summary>
    [MemoryPackable]
    public partial class RiftProgress : IGameNetworkMessage
    {
        public int Id { get; set; }
        public float[] Progress { get; set; } = System.Array.Empty<float>();
        public float TimeLeft { get; set; }
    }

    /// <summary>
    /// Host: the rift is over, cleared or failed (each player then picks a pact or a curse)
    /// </summary>
    [MemoryPackable]
    public partial class RiftEnded : IGameNetworkMessage
    {
        public int Id { get; set; }
        public bool Cleared { get; set; }
    }
}
