using MegabonkTogether.Common.Models;
using MemoryPack;

namespace MegabonkTogether.Common.Messages
{
    [MemoryPackable]
    public partial class LobbyUpdates : IGameNetworkMessage
    {
        /// <summary>
        /// A player's Inventory is null when it did not change since the last one sent (receivers keep the last one)
        /// </summary>
        public IEnumerable<Player> Players = new List<Player>();
        // Enemies: see EnemiesUpdate
        public IEnumerable<BossOrbModel> BossOrbs = new List<BossOrbModel>();
    }
}
