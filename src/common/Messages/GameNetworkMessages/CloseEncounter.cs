using MemoryPack;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    [MemoryPackable]
    public partial class CloseEncounter : IGameNetworkMessage
    {
        public uint Seq { get; set; }
    }
}
