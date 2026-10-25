using MemoryPack;

namespace Megaphonk.Common.Messages.WsMessages
{
    [MemoryPackable]
    public partial class GameStarting : IWsMessage
    {
        public uint ConnectionId { get; set; }
    }
}
