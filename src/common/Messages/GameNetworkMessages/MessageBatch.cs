using MemoryPack;
using System.Collections.Generic;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// Several small messages sent in one packet (one per destination and delivery method per frame).
    /// Handled as if each message had arrived on its own, in order. Never contains another batch.
    /// Only one member: senders build it from already serialized messages (see UdpClientService).
    /// </summary>
    [MemoryPackable]
    public partial class MessageBatch : IGameNetworkMessage
    {
        public List<IGameNetworkMessage> Messages { get; set; } = new List<IGameNetworkMessage>();
    }
}
