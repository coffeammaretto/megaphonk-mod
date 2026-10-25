using MemoryPack;

namespace Megaphonk.Common.Messages.WsMessages
{
    [MemoryPackable]
    public partial class GameStartingResponse : IWsMessage
    {
        public bool IsSuccess { get; set; }
    }
}
