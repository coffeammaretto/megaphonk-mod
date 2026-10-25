using Megaphonk.Common.Models;
using MemoryPack;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    [MemoryPackable]
    public partial class PlayerRespawned : IGameNetworkMessage
    {
        public uint OwnerId { get; set; }
        public QuantizedVector3 Position { get; set; }
    }
}
