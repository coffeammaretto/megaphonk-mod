using MemoryPack;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// The host started one of the mod's run music tracks: the clients play the same one from the same position
    /// </summary>
    [MemoryPackable]
    public partial class MusicTrackChanged : IGameNetworkMessage
    {
        public string Track { get; set; }
        public float Position { get; set; }
    }
}
