using MemoryPack;
using System.Numerics;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class SpawnedChest : IGameNetworkMessage
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public uint ChestId { get; set; }
    }
}
