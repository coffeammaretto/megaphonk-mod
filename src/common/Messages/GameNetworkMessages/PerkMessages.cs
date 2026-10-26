using MemoryPack;
using System.Numerics;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// Pact and curse mechanics beyond stats. Hit perks run on the host (it simulates every player's hits), shot perks
    /// on the owner's machine.
    /// </summary>
    public enum Perk : byte
    {
        None = 0,

        // Boons
        Explosive = 1,
        Splitting = 2,
        ChainLightning = 3,
        Piercing = 4,
        Execute = 5,
        Mark = 6,
        Echo = 7,
        PoisonCloud = 8,

        // Curses
        Recoil = 20,
        Misfire = 21,
        VengefulDead = 22,
        BloodPrice = 23,
        ShortRange = 24,
        Unlucky = 25
    }

    public enum PerkFxKind : byte
    {
        Explosion = 0,

        /// <summary>
        /// A line from Position to each target
        /// </summary>
        Shards = 1,

        /// <summary>
        /// A zigzag from Position through each target in turn
        /// </summary>
        Lightning = 2,

        PoisonCloud = 3
    }

    /// <summary>
    /// A player's perks and their strength (taken pacts and curses added up), sent to the host whenever they change
    /// </summary>
    [MemoryPackable]
    public partial class PlayerPerks : IGameNetworkMessage
    {
        public uint OwnerId { get; set; }
        public Perk[] Perks { get; set; } = System.Array.Empty<Perk>();
        public float[] Strengths { get; set; } = System.Array.Empty<float>();
    }

    /// <summary>
    /// Host: a perk's visual, the damage itself comes with the usual enemy damage
    /// </summary>
    [MemoryPackable]
    public partial class PerkFx : IGameNetworkMessage
    {
        public PerkFxKind Kind { get; set; }
        public Vector3 Position { get; set; }
        public float Radius { get; set; }
        public float Duration { get; set; }
        public Vector3[] Targets { get; set; } = System.Array.Empty<Vector3>();
    }
}
