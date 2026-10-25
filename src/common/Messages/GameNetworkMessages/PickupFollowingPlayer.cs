using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class PickupFollowingPlayer : IGameNetworkMessage
    {
        public uint PickupId { get; set; }
        public uint PlayerId { get; set; }
    }
}
