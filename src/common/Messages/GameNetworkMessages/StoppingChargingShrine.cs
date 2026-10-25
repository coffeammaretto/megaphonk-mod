using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class StoppingChargingShrine : IGameNetworkMessage
    {
        public uint ShrineNetplayId;
        public uint PlayerChargingId;
    }
}