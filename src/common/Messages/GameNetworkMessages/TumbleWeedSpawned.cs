using Megaphonk.Common.Models;
using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class TumbleWeedSpawned : IGameNetworkMessage
    {
        public uint NetplayId { get; set; }
        public QuantizedVector3 Position { get; set; }
        public QuantizedVector3 Velocity { get; set; }
    }
}
