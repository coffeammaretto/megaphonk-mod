using MemoryPack;

namespace Megaphonk.Common.Models
{
    [MemoryPackable]
    public partial class QuantizedRotation
    {
        public ushort QuantizedYaw { get; set; }
    }
}
