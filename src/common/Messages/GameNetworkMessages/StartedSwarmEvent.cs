using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class StartedSwarmEvent : IGameNetworkMessage
    {
        public float Duration;
    }
}
