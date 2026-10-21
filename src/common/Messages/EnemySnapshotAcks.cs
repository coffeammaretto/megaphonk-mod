namespace MegabonkTogether.Common.Messages
{
    /// <summary>
    /// Host: which enemies each EnemiesUpdate packet carried, to resend them when a client reports the packet lost.
    /// Loss recovery of the deltas: a lost send breaks that enemy's delta chain on that client, so its next send is absolute.
    /// Thread safe (packets are recorded on the main thread, acks arrive on the network thread).
    /// </summary>
    public sealed class EnemySnapshotHistory
    {
        private const int SIZE = 256; // ~10 s of packets
        private const int ACK_WINDOW = 64;

        private readonly object historyLock = new();
        private readonly uint[]?[] enemyIds = new uint[SIZE][];
        private readonly ushort[] seqs = new ushort[SIZE];
        private readonly Dictionary<uint, ushort> processedUpTo = new();
        private ushort nextSeq = 0;

        /// <summary>
        /// The sequence of the next packet, which carries these enemies
        /// </summary>
        public ushort Record(uint[] ids)
        {
            lock (historyLock)
            {
                var seq = ++nextSeq;
                enemyIds[seq % SIZE] = ids;
                seqs[seq % SIZE] = seq;
                return seq;
            }
        }

        /// <summary>
        /// A client's ack: adds to <paramref name="lostIds"/> the enemies of the packets it missed since its previous ack.
        /// A packet older than the newest received and not in the mask counts as lost (a late one only costs an absolute send).
        /// </summary>
        public void OnAck(uint client, ushort latest, ulong receivedMask, List<uint> lostIds)
        {
            lock (historyLock)
            {
                if (!processedUpTo.TryGetValue(client, out var processed))
                {
                    processedUpTo[client] = latest; //First ack: nothing to compare with
                    return;
                }

                var advance = (ushort)(latest - processed);
                if (advance == 0 || advance > ushort.MaxValue / 2)
                {
                    return; //Same or older ack (reordered)
                }

                // Gaps too old for the history were covered by keyframes since
                var first = advance > SIZE ? (ushort)(latest - SIZE + 1) : (ushort)(processed + 1);
                for (var seq = first; seq != latest; seq++)
                {
                    var offset = (ushort)(latest - seq);
                    var received = offset < ACK_WINDOW && ((receivedMask >> offset) & 1UL) != 0;
                    if (!received && seqs[seq % SIZE] == seq && enemyIds[seq % SIZE] is { } ids)
                    {
                        lostIds.AddRange(ids);
                    }
                }

                processedUpTo[client] = latest;
            }
        }
    }

    /// <summary>
    /// Client: the EnemiesUpdate packets received, for EnemiesAck. Thread safe.
    /// </summary>
    public sealed class EnemySnapshotAckBuilder
    {
        private readonly object ackLock = new();
        private bool hasReceived;
        private bool hasNews;
        private ushort latest;
        private ulong mask;

        public void OnReceived(ushort seq)
        {
            lock (ackLock)
            {
                hasNews = true;
                if (!hasReceived)
                {
                    hasReceived = true;
                    latest = seq;
                    mask = 1;
                    return;
                }

                var diff = (short)(seq - latest);
                if (diff > 0)
                {
                    mask = diff >= 64 ? 0 : mask << diff;
                    mask |= 1;
                    latest = seq;
                }
                else if (-diff < 64)
                {
                    mask |= 1UL << -diff;
                }
            }
        }

        /// <summary>
        /// The ack to send, if something arrived since the last one
        /// </summary>
        public bool TryBuild(out ushort latestSeq, out ulong receivedMask)
        {
            lock (ackLock)
            {
                latestSeq = latest;
                receivedMask = mask;
                var news = hasNews;
                hasNews = false;
                return news;
            }
        }
    }
}
