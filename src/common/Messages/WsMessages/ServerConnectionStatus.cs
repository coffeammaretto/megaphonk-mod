using Megaphonk.Common.Messages.WsMessages;
using MemoryPack;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class ServerConnectionStatus : IWsMessage
    {
        public bool HasJoined { get; set; }
        public uint ConnectionId { get; set; }
    }
}
