using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Utility;
using Megaphonk.Common.Messages.GameNetworkMessages;
using Assets.Scripts.Menu.Shop;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Megaphonk.Helpers;

namespace Megaphonk.Services
{
    /// <summary>
    /// Stat modifiers the mod adds to the local player for party rules (enemy amount, run modifiers, team buff...).
    /// Kept as named moving stats, refreshed periodically: added, updated or removed when the wanted set changes.
    /// Enemy related stats only matter on the host, which spawns the enemies.
    /// </summary>
    public interface IPartyModifiersService
    {
        public void Tick();
        public void Reset();
    }

    internal class PartyModifiersService(IUdpClientService udpClientService, IPartySettingsService partySettingsService, ITeamMeterService teamMeterService, IAscensionService ascensionService, ICurseService curseService) : IPartyModifiersService
    {
        private const float TICK_INTERVAL = 0.5f;
        private const string PREFIX = "Megaphonk_Party_";

        // Caps on the player's bonuses from all run modifiers together (additions to the multipliers)
        private const float MAX_XP_GAIN = 0.5f;
        private const float MAX_GOLD_BONUS = 0.5f;
        private const float MAX_DAMAGE_BONUS = 0.5f;
        private const float MAX_MOVE_SPEED_BONUS = 0.3f;
        private const float MAX_LUCK_BONUS = 0.5f;

        private readonly Dictionary<string, (EStat Stat, EStatModifyType Type, float Value)> applied = [];
        private IntPtr appliedInventory = IntPtr.Zero;
        private RunModifier announcedModifiers = RunModifier.None;
        private float nextTickAt = 0f;

        public void Tick()
        {
            var now = Time.realtimeSinceStartup;
            if (now < nextTickAt)
            {
                return;
            }
            nextTickAt = now + TICK_INTERVAL;

            var inventory = GameManager.Instance?.player?.inventory;
            if (inventory == null || inventory.statInventory == null)
            {
                return;
            }

            if (inventory.Pointer != appliedInventory) //New run: nothing applied on this inventory yet
            {
                applied.Clear();
                appliedInventory = inventory.Pointer;
            }

            try
            {
                Apply(inventory.statInventory, GetWantedModifiers());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Party modifiers tick failed: {ex}");
            }
        }

        private Dictionary<string, (EStat Stat, EStatModifyType Type, float Value)> GetWantedModifiers()
        {
            var wanted = new Dictionary<string, (EStat, EStatModifyType, float)>();
            var settings = partySettingsService.Current;
            var isHost = udpClientService.IsHost() ?? false;

            if (isHost && Mathf.Abs(settings.EnemyAmountMultiplier - 1f) > 0.001f)
            {
                wanted["EnemyAmount"] = (EStat.EnemyAmountMultiplier, EStatModifyType.Addition, settings.EnemyAmountMultiplier - 1f);
            }

            AddRunModifiers(wanted, settings.RunModifiers, isHost);

            if (isHost && ascensionService.Level > 0)
            {
                var level = ascensionService.Level;
                wanted["AscensionEnemyHp"] = (EStat.EnemyHpMultiplier, EStatModifyType.Addition, Mathf.Pow(settings.AscensionEnemyHpGrowth, level) - 1f);
                wanted["AscensionEnemyDamage"] = (EStat.EnemyDamageMultiplier, EStatModifyType.Addition, Mathf.Pow(settings.AscensionEnemyDamageGrowth, level) - 1f);
            }

            curseService.AddModifiers(wanted); //The local player's own curses, on every machine

            if (teamMeterService.IsBuffActive)
            {
                wanted["TeamBuffDamage"] = (EStat.DamageMultiplier, EStatModifyType.Addition, 0.25f);
                wanted["TeamBuffSpeed"] = (EStat.MoveSpeedMultiplier, EStatModifyType.Addition, 0.15f);
            }

            return wanted;
        }

        /// <summary>
        /// Player stats on every machine, enemy stats (and difficulty, summed across the party) on the host only
        /// </summary>
        private void AddRunModifiers(Dictionary<string, (EStat, EStatModifyType, float)> wanted, RunModifier modifiers, bool isHost)
        {
            if (modifiers == RunModifier.None)
            {
                announcedModifiers = RunModifier.None;
                return;
            }

            if (modifiers != announcedModifiers)
            {
                announcedModifiers = modifiers;
                Plugin.StartNotification(("Megaphonk", "RunModifiers"), ("Megaphonk", "RunModifiers_Description"), [string.Join(", ", Scripts.Modal.NetplayOptionsModal.ModifierList.Where(m => modifiers.HasFlag(m.Flag)).Select(m => Loc.T(m.Name)))]);
                Plugin.Log.LogInfo($"Run modifiers: {modifiers}");
            }

            // Sum every enabled modifier per stat, then cap the player's bonuses: modifiers stay worth it alone,
            // and stacking them all makes a hard run instead of a character that outscales everything
            var player = new Dictionary<EStat, float>();
            var enemy = new Dictionary<EStat, float>();
            var party = new Dictionary<EStat, float>();

            void Add(Dictionary<EStat, float> totals, EStat stat, float value) => totals[stat] = totals.GetValueOrDefault(stat) + value;

            if (modifiers.HasFlag(RunModifier.GlassCannon))
            {
                Add(player, EStat.DamageMultiplier, 0.5f);
                Add(player, EStat.MaxHealth, -0.4f);
            }

            // No modifier changes the number of enemies: it stays the vanilla one (the late game is heavy enough)
            if (modifiers.HasFlag(RunModifier.Horde))
            {
                Add(enemy, EStat.EnemyHpMultiplier, 0.5f);
                Add(player, EStat.XpIncreaseMultiplier, 0.25f);
                Add(player, EStat.GoldIncreaseMultiplier, 0.15f);
            }

            if (modifiers.HasFlag(RunModifier.ThickSkinned))
            {
                Add(enemy, EStat.EnemyHpMultiplier, 1f);
                Add(party, EStat.Luck, 0.25f);
            }

            if (modifiers.HasFlag(RunModifier.Frenzy))
            {
                Add(player, EStat.GoldIncreaseMultiplier, 0.2f);
                var steps = Mathf.FloorToInt(Math.Max(0f, MyTime.runTimer) / 300f);
                if (steps > 0)
                {
                    Add(enemy, EStat.EnemySpeedMultiplier, 0.1f * steps);
                }
            }

            if (modifiers.HasFlag(RunModifier.Lucky))
            {
                Add(party, EStat.Luck, 0.3f);
                Add(party, EStat.Difficulty, 0.3f);
            }

            if (modifiers.HasFlag(RunModifier.Poverty))
            {
                Add(player, EStat.GoldIncreaseMultiplier, -0.5f);
                Add(player, EStat.XpIncreaseMultiplier, 0.15f);
            }

            if (modifiers.HasFlag(RunModifier.Colossus))
            {
                Add(enemy, EStat.EnemyHpMultiplier, 0.5f);
                Add(player, EStat.XpIncreaseMultiplier, 0.25f);
            }

            if (modifiers.HasFlag(RunModifier.BrittleSwarm))
            {
                Add(enemy, EStat.EnemyHpMultiplier, -0.4f);
                Add(enemy, EStat.EnemyDamageMultiplier, 0.4f);
                Add(player, EStat.XpIncreaseMultiplier, -0.1f);
            }

            if (modifiers.HasFlag(RunModifier.Bloodbath))
            {
                Add(enemy, EStat.EnemyDamageMultiplier, 0.5f);
                Add(player, EStat.HealingMultiplier, -0.5f);
                Add(player, EStat.GoldIncreaseMultiplier, 0.25f);
            }

            if (modifiers.HasFlag(RunModifier.Pinball))
            {
                Add(player, EStat.KnockbackMultiplier, 1f);
                Add(player, EStat.MoveSpeedMultiplier, 0.15f);
            }

            if (modifiers.HasFlag(RunModifier.SilverRush))
            {
                Add(player, EStat.SilverIncreaseMultiplier, 0.5f);
                Add(enemy, EStat.EnemyDamageMultiplier, 0.25f);
            }

            if (modifiers.HasFlag(RunModifier.Overdrive))
            {
                Add(player, EStat.MoveSpeedMultiplier, 0.2f);
                Add(player, EStat.ProjectileSpeedMultiplier, 0.25f);
                Add(enemy, EStat.EnemySpeedMultiplier, 0.2f);
            }

            if (modifiers.HasFlag(RunModifier.Shockwave))
            {
                Add(player, EStat.SizeMultiplier, 0.25f);
                Add(player, EStat.DamageMultiplier, -0.2f);
            }

            if (modifiers.HasFlag(RunModifier.RisingTide))
            {
                Add(enemy, EStat.EnemyScalingMultiplier, 0.5f);
                Add(player, EStat.ChestPriceMultiplier, -0.25f);
            }

            Cap(player, EStat.XpIncreaseMultiplier, MAX_XP_GAIN);
            Cap(player, EStat.GoldIncreaseMultiplier, MAX_GOLD_BONUS);
            Cap(player, EStat.DamageMultiplier, MAX_DAMAGE_BONUS);
            Cap(player, EStat.MoveSpeedMultiplier, MAX_MOVE_SPEED_BONUS);
            Cap(party, EStat.Luck, MAX_LUCK_BONUS);

            foreach (var (stat, value) in player)
            {
                if (value != 0f)
                {
                    wanted["Run" + stat] = (stat, EStatModifyType.Addition, value);
                }
            }

            if (isHost) //Enemies are simulated by the host; luck and difficulty are combined across the party: added once, on the host
            {
                foreach (var (stat, value) in enemy)
                {
                    if (value != 0f)
                    {
                        wanted["Run" + stat] = (stat, EStatModifyType.Addition, value);
                    }
                }

                foreach (var (stat, value) in party)
                {
                    if (value != 0f)
                    {
                        wanted["Run" + stat] = (stat, EStatModifyType.Flat, value);
                    }
                }
            }
        }

        private static void Cap(Dictionary<EStat, float> totals, EStat stat, float max)
        {
            if (totals.TryGetValue(stat, out var value) && value > max)
            {
                totals[stat] = max;
            }
        }

        private void Apply(StatInventory statInventory, Dictionary<string, (EStat Stat, EStatModifyType Type, float Value)> wanted)
        {
            foreach (var (key, modifier) in wanted)
            {
                if (applied.TryGetValue(key, out var current) && current == modifier
                    && statInventory.movingStats != null && statInventory.movingStats.ContainsKey(PREFIX + key)) //The game may rebuild its moving stats
                {
                    continue;
                }

                statInventory.ChangeMovingStat(PREFIX + key, new StatModifier
                {
                    stat = modifier.Stat,
                    modifyType = modifier.Type,
                    modification = modifier.Value
                });
                applied[key] = modifier;
                Plugin.Log.LogInfo($"Party modifier {key}: {modifier.Stat} {modifier.Type} {modifier.Value}");
            }

            foreach (var key in applied.Keys.Where(key => !wanted.ContainsKey(key)).ToList())
            {
                statInventory.RemoveMovingStat(PREFIX + key);
                applied.Remove(key);
                Plugin.Log.LogInfo($"Party modifier {key} removed");
            }
        }

        public void Reset()
        {
            applied.Clear();
            appliedInventory = IntPtr.Zero;
            announcedModifiers = RunModifier.None;
            nextTickAt = 0f;
        }
    }
}
