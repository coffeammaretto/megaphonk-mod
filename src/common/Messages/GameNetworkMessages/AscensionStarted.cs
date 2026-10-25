using MemoryPack;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// The host took the final portal in a co-op run: everyone goes back to the first stage of the map, builds kept, with
    /// stronger enemies (ascension Level, 1 for the first loop)
    /// </summary>
    [MemoryPackable]
    public partial class AscensionStarted : IGameNetworkMessage
    {
        public int Level { get; set; }
    }
}
