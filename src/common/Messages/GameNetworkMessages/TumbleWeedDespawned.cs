using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class TumbleWeedDespawned : IGameNetworkMessage
    {
        public uint NetplayId { get; set; }
    }
}
