using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class ClientInGameReady : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
    }
}
