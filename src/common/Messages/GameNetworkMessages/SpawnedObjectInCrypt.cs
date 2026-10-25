using Megaphonk.Common.Models;
using MemoryPack;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    [MemoryPackable]
    public partial class SpawnedObjectInCrypt : IGameNetworkMessage
    {
        public uint NetplayId { get; set; }
        public QuantizedVector3 Position { get; set; }
        public bool IsCryptLeave { get; set; }
    }
}
