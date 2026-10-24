using MemoryPack;

namespace MegabonkTogether.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// Sent by every player when a game starts: builds with different network messages silently drop each other's messages
    /// </summary>
    [MemoryPackable]
    public partial class ModVersionInfo : IGameNetworkMessage
    {
        /// <summary>
        /// Change it whenever the network messages change
        /// </summary>
        public const string CurrentVersion = "2.0.15";

        public uint OwnerId { get; set; }
        public string Version { get; set; }
    }
}
