using MemoryPack;
using System.Numerics;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class SpawnedPickupOrb : IGameNetworkMessage
    {
        public int Pickup { get; set; }
        public Vector3 Position { get; set; }
    }
}
