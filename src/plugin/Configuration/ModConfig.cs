using BepInEx.Configuration;
using MegabonkTogether.Common.Messages.GameNetworkMessages;

namespace MegabonkTogether.Configuration
{
    public static class ModConfig
    {
        private static ConfigFile configFile;

        // DEV_URL is "ws://127.0.0.1:5000"

        public static ConfigEntry<string> PlayerName { get; private set; }
        public static ConfigEntry<bool> CheckForUpdates { get; private set; }
        public static ConfigEntry<string> ServerUrl { get; private set; }
        public static ConfigEntry<uint> RDVServerPort { get; private set; }
        public static ConfigEntry<bool> UseSteamNetworking { get; private set; }
        public static ConfigEntry<bool> ShowChangelog { get; private set; }
        public static ConfigEntry<string> PreviousVersion { get; private set; }
        public static ConfigEntry<bool> AllowSavesDuringNetplay { get; private set; }
        public static ConfigEntry<bool> EnabledSharedExperience { get; private set; }
        public static ConfigEntry<float> RewardWindowInputDelay { get; private set; }
        public static ConfigEntry<UnityEngine.KeyCode> PingKey { get; private set; }

        // Party rules: the host's values apply to everyone
        public static ConfigEntry<bool> ScaleFreeChestsWithPlayers { get; private set; }
        public static ConfigEntry<float> ChestPricePerExtraPlayer { get; private set; }
        public static ConfigEntry<PartyStatMode> PartyStatMode { get; private set; }
        public static ConfigEntry<float> PartyLuckCap { get; private set; }
        public static ConfigEntry<float> PartyDifficultyCap { get; private set; }
        public static ConfigEntry<float> EnemyHpPerExtraPlayer { get; private set; }
        public static ConfigEntry<float> EnemyAmountMultiplier { get; private set; }
        public static ConfigEntry<RewardPauseMode> RewardPauseMode { get; private set; }
        public static ConfigEntry<bool> DownedEnabled { get; private set; }
        public static ConfigEntry<float> DownedBleedoutSeconds { get; private set; }
        public static ConfigEntry<float> DownedReviveSeconds { get; private set; }
        public static ConfigEntry<float> DownedReviveRadius { get; private set; }
        public static ConfigEntry<float> DownedReviveHpFraction { get; private set; }
        public static ConfigEntry<float> SharedPassiveRadius { get; private set; }
        public static ConfigEntry<bool> TeamMeterEnabled { get; private set; }
        public static ConfigEntry<float> TeamMeterKillsToFill { get; private set; }
        public static ConfigEntry<float> TeamMeterGrowth { get; private set; }
        public static ConfigEntry<float> TeamBuffSeconds { get; private set; }
        public static ConfigEntry<RunModifier> RunModifiers { get; private set; }
        public static ConfigEntry<bool> ChallengesEnabled { get; private set; }

        public static ConfigEntry<bool> CustomMusic { get; private set; }
        public static ConfigEntry<bool> SubmitCoopRuns { get; private set; }
        public static ConfigEntry<string> LeaderboardUrl { get; private set; }

        public static ConfigEntry<bool> PerformanceLog { get; private set; }
        public static ConfigEntry<float> PerformanceSpikeMs { get; private set; }
        public static ConfigEntry<bool> KeepBepInExConsole { get; private set; }
        public static ConfigEntry<bool> NativeTransformHooks { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            configFile = config;

            PlayerName = config.Bind(
                "Player",
                "PlayerName",
                "Player",
                "Your display name shown to other players. Please be respectful!"
            );
            CheckForUpdates = config.Bind(
                "Updates",
                "CheckForUpdates",
                true,
                "Check for updates on startup. Recommended to leave enabled"
            );
            ServerUrl = config.Bind(
                "Network",
                "ServerUrl",
                "wss://megabonk-together-matchmaking.balatro-vs-matchmaking.eu",
                "The URL of the matchmaking server. Do not change this unless you know what you're doing (e.g. for self-hosting). Use ws://127.0.0.1:5000 on localhost for testing purpose"
            );
            RDVServerPort = config.Bind(
                "Network",
                "RDVServerPort",
                (uint)5678,
                "The port of the relay server. Do not change this unless you know what you're doing"
            );
            UseSteamNetworking = config.Bind(
                "Network",
                "UseSteamNetworking",
                true,
                "Friendlies rooms go through Steam (Steam lobby and Steam relays, no matchmaking server). Every player of a room must use the same setting. Random matchmaking always uses the server."
            );
            ShowChangelog = config.Bind(
                "Updates",
                "ShowChangelog",
                false,
                "Internal flag to show changelog after an update. Do not modify manually."
            );
            PreviousVersion = config.Bind(
                "Updates",
                "PreviousVersion",
                "",
                "Internal flag to store the previous version before an update. Do not modify manually."
            );
            AllowSavesDuringNetplay = config.Bind(
                "Gameplay",
                "AllowSavesDuringNetplay",
                true,
                "Allow game saves during netplay sessions."
            );
            EnabledSharedExperience = config.Bind(
                "Gameplay",
                "EnabledSharedExperience",
                true,
                "Enable Host experience (Same XP and pause enabled). Disable for no pause and separate XP."
            );
            RewardWindowInputDelay = config.Bind(
                "Gameplay",
                "RewardWindowInputDelay",
                1f,
                "Seconds during which clicks and presses are ignored after a level up, chest or other reward window appears in netplay, to prevent accidental picks. 0 to disable."
            );

            PingKey = config.Bind("Controls", "PingKey", UnityEngine.KeyCode.Mouse2,
                "Key to mark the spot you look at for the whole team (ping).");

            const string party = "Party (host settings, apply to everyone)";
            ScaleFreeChestsWithPlayers = config.Bind(party, "ScaleFreeChestsWithPlayers", false,
                "Spawn more free chests with more players. Off by default with shared experience: every chest already gives an item to everyone.");
            ChestPricePerExtraPlayer = config.Bind(party, "ChestPricePerExtraPlayer", 0.25f,
                "Shared experience: chest price increase per player beyond the first (0.25 = +25%), since a bought chest gives an item to everyone.");
            PartyStatMode = config.Bind(party, "PartyStatMode", Common.Messages.GameNetworkMessages.PartyStatMode.Sum,
                "How luck and difficulty combine across the party: Sum, MaxPlusHalf (highest + half of the others) or Average.");
            PartyLuckCap = config.Bind(party, "PartyLuckCap", 0f,
                "Maximum party luck, 0 for no cap.");
            PartyDifficultyCap = config.Bind(party, "PartyDifficultyCap", 0f,
                "Maximum party difficulty, 0 for no cap.");
            EnemyHpPerExtraPlayer = config.Bind(party, "EnemyHpPerExtraPlayer", 0.1f,
                "Enemy HP increase per alive player beyond the first (0.1 = +10%).");
            EnemyAmountMultiplier = config.Bind(party, "EnemyAmountMultiplier", 1f,
                "Multiplier on the amount of enemies (1 = unchanged).");
            RewardPauseMode = config.Bind(party, "RewardPauseMode", Common.Messages.GameNetworkMessages.RewardPauseMode.Party,
                "Shared experience: Party = everyone pauses while someone picks a reward, Own = pick without pausing while invulnerable, the others keep playing.");
            DownedEnabled = config.Bind(party, "DownedEnabled", true,
                "Lethal damage downs a player instead of killing it: it bleeds out and a teammate standing next to it revives it.");
            DownedBleedoutSeconds = config.Bind(party, "DownedBleedoutSeconds", 25f,
                "Seconds a downed player has before dying.");
            DownedReviveSeconds = config.Bind(party, "DownedReviveSeconds", 3f,
                "Seconds a teammate must stay next to a downed player to revive it.");
            // New key: the old one kept the previous default (4) in existing config files
            DownedReviveRadius = config.Bind(party, "DownedReviveRadiusMeters", 8f,
                "How close (meters) a teammate must be to revive a downed player.");
            DownedReviveHpFraction = config.Bind(party, "DownedReviveHpFraction", 0.3f,
                "Fraction of max HP a revived player gets back.");
            SharedPassiveRadius = config.Bind(party, "SharedPassiveRadius", 0f,
                "Character passives of the other players apply within this distance (meters) of their owner. 0 = everywhere.");
            TeamMeterEnabled = config.Bind(party, "TeamMeterEnabled", true,
                "Team meter: kills fill it while at least two players killed in the last seconds. Full = damage and speed buff for everyone.");
            TeamMeterKillsToFill = config.Bind(party, "TeamMeterKillsToFill", 250f,
                "Team kills needed to fill the team meter the first time.");
            TeamMeterGrowth = config.Bind(party, "TeamMeterGrowth", 1.5f,
                "The kills needed are multiplied by this after each team buff.");
            TeamBuffSeconds = config.Bind(party, "TeamBuffSeconds", 10f,
                "Team buff duration (+25% damage, +15% move speed).");
            RunModifiers = config.Bind(party, "RunModifiers", RunModifier.None,
                "Run modifiers, comma separated: GlassCannon (damage +50%, max HP -40%), Horde (enemy HP +50%, XP +25%, gold +15%), ThickSkinned (enemy HP x2, luck +0.25), Frenzy (enemies 10% faster every 5 minutes, gold +20%), Lucky (luck +0.3, difficulty +0.3), Poverty (gold -50%, XP +15%), Colossus (enemy HP +50%, XP +25%), BrittleSwarm (enemy HP -40%, enemy damage +40%, XP -10%), Bloodbath (enemy damage +50%, healing -50%, gold +25%), Pinball (knockback x2, move speed +15%), SilverRush (silver +50%, enemy damage +25%), Overdrive (move speed +20%, projectile speed +25%, enemies 20% faster), Shockwave (weapon area +25%, damage -20%), RisingTide (enemies grow stronger 50% faster, chests 25% cheaper). Combined bonuses are capped: XP gain +50%, luck +0.5, gold +50%, damage +50%, move speed +30%. None for a normal run.");
            ChallengesEnabled = config.Bind(party, "ChallengesEnabled", true,
                "Coop challenges rewarded with chests on the next stage: one for each player with no death on the stage, one for each player who survived the stage boss fight.");

            CustomMusic = config.Bind("Music", "CustomMusic", true,
                "Play your own music in runs: the mp3 files of the Music folder (next to the mod's dll) play in a random order instead of the stage music. Boss and menu music stay the game's. Nothing happens when the folder is empty.");

            SubmitCoopRuns = config.Bind("Leaderboard", "SubmitCoopRuns", true,
                "Host: submit each finished co-op run to the Megaphonk co-op leaderboard (map, tier, players' names, Steam IDs, characters and kills). The game's own leaderboards do not count co-op runs.");
            LeaderboardUrl = config.Bind("Leaderboard", "LeaderboardUrl", "https://megaphonk-leaderboard.megaphonk-leaderboard.workers.dev",
                "The URL of the co-op leaderboard. Do not change this unless you know what you're doing (e.g. for self-hosting).");

            PerformanceLog = config.Bind("Diagnostics", "PerformanceLog", true,
                "Log the time the mod spends each frame (report every 10 s, slow frames) to find what causes lag. Small overhead, leave off when not needed. Restart the game after changing.");
            PerformanceSpikeMs = config.Bind("Diagnostics", "PerformanceSpikeMs", 50f,
                "Performance log: frames longer than this (milliseconds) are logged with the mod's share.");
            KeepBepInExConsole = config.Bind("Diagnostics", "KeepBepInExConsole", false,
                "Keep the BepInEx console window. Off: the mod turns it off (from the next launch), writing to it stalls the game when a lot is logged. The log file is still written.");
            NativeTransformHooks = config.Bind("Diagnostics", "NativeTransformHooks", true,
                "Hook Component.transform, Transform.position and Transform.rotation natively (much faster in the late game). Set to false if the game crashes on startup since 2.0.13: the slower Harmony patches are used instead.");
        }

        public static PartySettings ToPartySettings()
        {
            return new PartySettings
            {
                ScaleFreeChestsWithPlayers = ScaleFreeChestsWithPlayers.Value,
                ChestPricePerExtraPlayer = ChestPricePerExtraPlayer.Value,
                PartyStatMode = PartyStatMode.Value,
                PartyLuckCap = PartyLuckCap.Value,
                PartyDifficultyCap = PartyDifficultyCap.Value,
                EnemyHpPerExtraPlayer = EnemyHpPerExtraPlayer.Value,
                EnemyAmountMultiplier = EnemyAmountMultiplier.Value,
                RewardPauseMode = RewardPauseMode.Value,
                DownedEnabled = DownedEnabled.Value,
                DownedBleedoutSeconds = DownedBleedoutSeconds.Value,
                DownedReviveSeconds = DownedReviveSeconds.Value,
                DownedReviveRadius = DownedReviveRadius.Value,
                DownedReviveHpFraction = DownedReviveHpFraction.Value,
                SharedPassiveRadius = SharedPassiveRadius.Value,
                TeamMeterEnabled = TeamMeterEnabled.Value,
                TeamMeterKillsToFill = TeamMeterKillsToFill.Value,
                TeamMeterGrowth = TeamMeterGrowth.Value,
                TeamBuffSeconds = TeamBuffSeconds.Value,
                RunModifiers = RunModifiers.Value,
                ChallengesEnabled = ChallengesEnabled.Value,
            };
        }

        public static void Save()
        {
            configFile?.Save();
        }
    }
}
