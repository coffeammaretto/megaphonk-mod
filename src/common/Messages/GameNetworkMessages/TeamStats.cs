using MemoryPack;
using System.Collections.Generic;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// Client to host: gold the client earned so far this run, and its Steam id with the session ticket proving it
    /// (co-op leaderboard)
    /// </summary>
    [MemoryPackable]
    public partial class RunStatsReport : IGameNetworkMessage
    {
        public uint OwnerId { get; set; }
        public int GoldEarned { get; set; }
        public ulong SteamId { get; set; }
        public string SteamTicket { get; set; }
    }

    /// <summary>
    /// Host to everyone at the end of a run: what each player did
    /// </summary>
    [MemoryPackable]
    public partial class TeamStats : IGameNetworkMessage
    {
        public List<TeamStatsEntry> Entries { get; set; } = new List<TeamStatsEntry>();
    }

    [MemoryPackable]
    public partial class TeamStatsEntry
    {
        public string Name { get; set; }
        public float Damage { get; set; }
        public int Kills { get; set; }
        public int GoldEarned { get; set; }
    }
}
