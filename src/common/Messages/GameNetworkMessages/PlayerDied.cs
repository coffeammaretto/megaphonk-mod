using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class PlayerDied : IGameNetworkMessage
    {
        public uint PlayerId { get; set; }
    }
}
