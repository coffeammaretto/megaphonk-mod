using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class FinalBossOrbSpawned : IGameNetworkMessage
    {
        public Orb OrbType { get; set; }
        public uint Target { get; set; }
        public uint OrbId { get; set; }
    }

    public enum  Orb
    {
        Bleed,
        Following,
        Shooty
    }
}
