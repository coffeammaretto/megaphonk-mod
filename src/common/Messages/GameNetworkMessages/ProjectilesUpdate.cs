using Megaphonk.Common.Models;
using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class ProjectilesUpdate : IGameNetworkMessage
    {
        public ICollection<Projectile> Projectiles { get; set; } = new List<Projectile>();
    }
}
