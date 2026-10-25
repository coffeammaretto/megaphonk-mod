using Megaphonk.Common.Models;
using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class TumbleWeedsUpdate : IGameNetworkMessage
    {
        public TumbleWeedModel[] TumbleWeeds { get; set; }
    }
}
