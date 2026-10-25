using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class PlayerDisconnected : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
    }
}
