using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class StormStarted : IGameNetworkMessage
    {
        public float StormOverAtTime { get; set; }
    }
}
