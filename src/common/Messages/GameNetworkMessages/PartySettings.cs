using MemoryPack;

namespace Megaphonk.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// Party wide rules, decided by the host's config and sent to clients when a game starts or a setting changes
    /// </summary>
    [MemoryPackable]
    public partial class PartySettings : IGameNetworkMessage
    {
        // Chests
        public bool ScaleFreeChestsWithPlayers { get; set; } = false;
        public float ChestPricePerExtraPlayer { get; set; } = 0.25f;

        // Luck and difficulty shared across the party
        public PartyStatMode PartyStatMode { get; set; } = PartyStatMode.Sum;
        public float PartyLuckCap { get; set; } = 0f;
        public float PartyDifficultyCap { get; set; } = 0f;

        // Enemies
        public float EnemyHpPerExtraPlayer { get; set; } = 0.1f;
        public float EnemyAmountMultiplier { get; set; } = 1f;

        // Reward windows
        public RewardPauseMode RewardPauseMode { get; set; } = RewardPauseMode.Party;

        // Downed state
        public bool DownedEnabled { get; set; } = true;
        public float DownedBleedoutSeconds { get; set; } = 25f;
        public float DownedReviveSeconds { get; set; } = 3f;
        public float DownedReviveRadius { get; set; } = 8f;
        public float DownedReviveHpFraction { get; set; } = 0.3f;

        /// <summary>
        /// Character passives of the other players apply within this distance of their owner, 0 for everywhere
        /// </summary>
        public float SharedPassiveRadius { get; set; } = 0f;

        // Team meter
        public bool TeamMeterEnabled { get; set; } = true;
        public float TeamMeterKillsToFill { get; set; } = 250f;
        public float TeamMeterGrowth { get; set; } = 1.5f;
        public float TeamBuffSeconds { get; set; } = 10f;

        // Run modifiers picked by the host
        public RunModifier RunModifiers { get; set; } = RunModifier.None;

        public bool ChallengesEnabled { get; set; } = true;

        // Co-op endgame: an ascension portal next to the final portal takes the party back to the first stage
        public bool AscensionEnabled { get; set; } = true;
        public float AscensionEnemyHpGrowth { get; set; } = 1.5f;
        public float AscensionEnemyDamageGrowth { get; set; } = 1.25f;
        public int AscensionMaxEnemies { get; set; } = 300;

        // Testing shortcuts (DebugTools): the host's choice, so the whole party takes the same stage
        public bool DebugTools { get; set; } = false;
    }

    [System.Flags]
    public enum RunModifier
    {
        None = 0,

        /// <summary>
        /// Damage +50%, max HP -40%
        /// </summary>
        GlassCannon = 1 << 0,

        /// <summary>
        /// Enemy HP +50%, XP +25%, gold +15%
        /// </summary>
        Horde = 1 << 1,

        /// <summary>
        /// Enemy HP x2, luck +0.25
        /// </summary>
        ThickSkinned = 1 << 2,

        /// <summary>
        /// Enemies 10% faster every 5 minutes, gold +20%
        /// </summary>
        Frenzy = 1 << 3,

        /// <summary>
        /// Luck +0.3, difficulty +0.3
        /// </summary>
        Lucky = 1 << 4,

        /// <summary>
        /// Gold -50%, XP +15%
        /// </summary>
        Poverty = 1 << 5,

        /// <summary>
        /// Enemy HP +50%, XP +25%
        /// </summary>
        Colossus = 1 << 6,

        /// <summary>
        /// Enemy HP -40%, enemy damage +40%, XP -10%
        /// </summary>
        BrittleSwarm = 1 << 7,

        /// <summary>
        /// Enemy damage +50%, healing -50%, gold +25%
        /// </summary>
        Bloodbath = 1 << 8,

        /// <summary>
        /// Knockback x2, move speed +15%
        /// </summary>
        Pinball = 1 << 9,

        /// <summary>
        /// Silver +50%, enemy damage +25%
        /// </summary>
        SilverRush = 1 << 10,

        /// <summary>
        /// Move speed +20%, projectile speed +25%, enemies 20% faster
        /// </summary>
        Overdrive = 1 << 11,

        /// <summary>
        /// Weapon area +25%, damage -20%
        /// </summary>
        Shockwave = 1 << 12,

        /// <summary>
        /// Enemies grow stronger 50% faster, chests 25% cheaper
        /// </summary>
        RisingTide = 1 << 13,
    }

    public enum PartyStatMode : byte
    {
        Sum = 0,
        MaxPlusHalf = 1,
        Average = 2
    }

    public enum RewardPauseMode : byte
    {
        /// <summary>
        /// Everyone pauses while someone picks a reward
        /// </summary>
        Party = 0,

        /// <summary>
        /// Pick without pausing, invulnerable meanwhile; the others keep playing
        /// </summary>
        Own = 1
    }
}
