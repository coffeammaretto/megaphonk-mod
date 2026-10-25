using Megaphonk.Common.Messages.WsMessages;
using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class MatchmakingServerConnectionStatus : IWsMessage
    {
        public bool HasJoined { get; set; }
        public uint ConnectionId { get; set; }
        public string RoomCode { get; set; }
        public string Message { get; set; }
    }
}
