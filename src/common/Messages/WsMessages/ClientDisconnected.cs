using MemoryPack;

namespace Megaphonk.Common.Messages.WsMessages
{
    [MemoryPackable]
    public partial class ClientDisconnected : IWsMessage
    {
        public string RoomCode { get; set; }
        public uint ClientConnectionId { get; set; }
    }
}
