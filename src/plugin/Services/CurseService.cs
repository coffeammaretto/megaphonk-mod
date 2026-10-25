using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Utility;
using Megaphonk.Helpers;
using Megaphonk.Scripts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Megaphonk.Services
{
    public enum CurseTier
    {
        Lesser,
        Normal,
        Greater
    }

    /// <summary>
    /// One stat a curse lowers (or raises, for a price): an addition to the stat's multiplier, so a percentage of what
    /// the player has. A build without that stat doesn't care, which is the point of choosing.
    /// </summary>
    public record CurseEffect(EStat Stat, float Value, string Label);

    public record CurseDefinition(string Name, CurseEffect[] Effects);

    public record CurseOffer(CurseDefinition Curse, CurseTier Tier)
    {
        public float Scale => CurseService.GetTierScale(Tier);
    }

    /// <summary>
    /// Ascension curses: after each ascension, every player picks one of three random curses for themselves (each
    /// character has its own weaknesses). They stack for the rest of the run, on the local player's stats only, so
    /// nothing goes over the network. While choosing, the party waits as for a level up (shared reward pause).
    /// </summary>
    public interface ICurseService
    {
        public IReadOnlyList<CurseOffer> Taken { get; }

        /// <summary>
        /// An ascension started: a choice once the next stage is loaded
        /// </summary>
        public void QueueChoice(int ascensionLevel);

        /// <summary>
        /// The taken curses as stat modifiers (PartyModifiersService)
        /// </summary>
        public void AddModifiers(Dictionary<string, (EStat Stat, EStatModifyType Type, float Value)> wanted);

        public void Tick();
        public void Reset();
    }

    internal class CurseService : ICurseService
    {
        private const int OFFER_COUNT = 3;
        private const float OPEN_DELAY = 2f;
        private const float MAX_PENALTY = 0.75f; //Most a stat loses to curses together
        private const float MAX_INCREASE = 1.5f;

        // Stats are summed per stat, so a curse twice is twice the penalty
        private static readonly CurseDefinition[] Curses =
        [
            new("Frailty", [new(EStat.MaxHealth, -0.15f, "Max HP")]),
            new("Weakness", [new(EStat.DamageMultiplier, -0.12f, "Damage")]),
            new("Lethargy", [new(EStat.AttackSpeed, -0.12f, "Attack speed")]),
            new("Burden", [new(EStat.MoveSpeedMultiplier, -0.1f, "Movement speed")]),
            new("Rot", [new(EStat.HealingMultiplier, -0.3f, "Healing"), new(EStat.HealthRegen, -0.4f, "HP regen")]),
            new("Myopia", [new(EStat.PickupRange, -0.3f, "Pickup range")]),
            new("Avarice", [new(EStat.GoldIncreaseMultiplier, -0.25f, "Gold"), new(EStat.SilverIncreaseMultiplier, -0.25f, "Silver")]),
            new("Misfortune", [new(EStat.Luck, -0.2f, "Luck")]),
            new("Exposure", [new(EStat.Armor, -0.25f, "Armor")]),
            new("Stiffness", [new(EStat.Evasion, -0.3f, "Evasion")]),
            new("Fading", [new(EStat.DurationMultiplier, -0.15f, "Duration"), new(EStat.EffectDurationMultiplier, -0.2f, "Effect duration")]),
            new("Withering", [new(EStat.SizeMultiplier, -0.15f, "Size")]),
            new("Dull Edge", [new(EStat.CritChance, -0.2f, "Crit chance"), new(EStat.CritDamage, -0.15f, "Crit damage")]),
            new("Cracked Shield", [new(EStat.Shield, -0.35f, "Shield")]),
            new("Bloodless", [new(EStat.Lifesteal, -0.35f, "Lifesteal")]),
            new("Blunted Thorns", [new(EStat.Thorns, -0.4f, "Thorns")]),
            new("Thick Air", [new(EStat.ProjectileSpeedMultiplier, -0.2f, "Projectile speed"), new(EStat.KnockbackMultiplier, -0.3f, "Knockback")]),
            new("Cowardice", [new(EStat.EliteDamageMultiplier, -0.25f, "Elite damage")]),
            new("Elemental Silence", [new(EStat.FireDamage, -0.2f, "Fire damage"), new(EStat.IceDamage, -0.2f, "Ice damage"), new(EStat.LightningDamage, -0.2f, "Lightning damage"), new(EStat.PoisonDamageMultiplier, -0.2f, "Poison damage")]),
            new("Cold Ashes", [new(EStat.BurnChance, -0.3f, "Burn chance"), new(EStat.FreezeChance, -0.3f, "Freeze chance")]),
            new("Dim Power", [new(EStat.PowerupChance, -0.3f, "Power-up chance"), new(EStat.PowerupBoostMultiplier, -0.2f, "Power-up strength")]),
            new("Inflation", [new(EStat.ChestPriceMultiplier, 0.2f, "Chest price")]),
            new("Grounded", [new(EStat.JumpHeight, -0.25f, "Jump height"), new(EStat.MoveSpeedMultiplier, -0.05f, "Movement speed")]),
            new("Glass Bones", [new(EStat.MaxHealth, -0.08f, "Max HP"), new(EStat.Armor, -0.15f, "Armor")]),
            new("Tremor", [new(EStat.DamageMultiplier, -0.06f, "Damage"), new(EStat.CritChance, -0.1f, "Crit chance")]),
            new("Exhaustion", [new(EStat.AttackSpeed, -0.06f, "Attack speed"), new(EStat.MoveSpeedMultiplier, -0.06f, "Movement speed")]),
        ];

        private readonly IEncounterService encounterService;
        // Resolved when used: the synchronization service depends on services that depend on this one
        private readonly Lazy<ISynchronizationService> synchronization = new(() => Plugin.Services.GetService<ISynchronizationService>());
        private readonly System.Random random = new();
        private readonly List<CurseOffer> taken = [];
        private readonly Queue<int> pendingLevels = new();
        private CurseChoiceWindow window;
        private List<CurseOffer> offers;
        private bool isStageReady;
        private float openAt;

        public IReadOnlyList<CurseOffer> Taken => taken;

        public CurseService(IEncounterService encounterService)
        {
            this.encounterService = encounterService;

            EventManager.SubscribeGameStartedEvents(() =>
            {
                isStageReady = true;
                openAt = Time.realtimeSinceStartup + OPEN_DELAY;
            });
            EventManager.SubscribePortalOpenedEvents(() =>
            {
                isStageReady = false;
                CloseWindow(); //Left the stage before choosing: chosen on the next one
            });
        }

        public static float GetTierScale(CurseTier tier)
        {
            return tier switch
            {
                CurseTier.Lesser => 0.6f,
                CurseTier.Greater => 1.6f,
                _ => 1f
            };
        }

        public void QueueChoice(int ascensionLevel)
        {
            pendingLevels.Enqueue(ascensionLevel);
            isStageReady = false; //Not on the stage being left: once the next one started
        }

        public void AddModifiers(Dictionary<string, (EStat Stat, EStatModifyType Type, float Value)> wanted)
        {
            var totals = new Dictionary<EStat, float>();
            foreach (var offer in taken)
            {
                foreach (var effect in offer.Curse.Effects)
                {
                    totals[effect.Stat] = totals.GetValueOrDefault(effect.Stat) + effect.Value * offer.Scale;
                }
            }

            foreach (var (stat, total) in totals)
            {
                wanted["Curse" + stat] = (stat, EStatModifyType.Addition, Mathf.Clamp(total, -MAX_PENALTY, MAX_INCREASE));
            }
        }

        public void Tick()
        {
            if (window != null)
            {
                if (GameManager.Instance?.player == null || !synchronization.Value.HasNetplaySessionStarted())
                {
                    CloseWindow();
                    return;
                }

                window.Update();
                return;
            }

            if (pendingLevels.Count == 0 || !isStageReady || Time.realtimeSinceStartup < openAt)
            {
                return;
            }

            var player = GameManager.Instance?.player;
            var encounterWindows = UiManager.Instance?.encounterWindows;
            if (player == null || !player.playerInput.CanInput() || encounterWindows == null || encounterWindows.encounterInProgress
                || !synchronization.Value.HasNetplaySessionStarted())
            {
                return; //After the stage intro, and never over a reward window
            }

            Open(pendingLevels.Peek());
        }

        private void Open(int level)
        {
            offers = Curses.OrderBy(_ => random.Next()).Take(OFFER_COUNT).Select(curse => new CurseOffer(curse, RollTier())).ToList();
            window = CurseChoiceWindow.Open(level, offers, taken, OnChosen);
            if (window == null)
            {
                openAt = Time.realtimeSinceStartup + OPEN_DELAY; //No UI yet: try again
                return;
            }

            encounterService.IsCurseChoiceOpen = true;
            if (synchronization.Value.IsPartyPauseEnabled())
            {
                MyTime.Pause(); //The host pauses the others when it sees this player choosing
            }
            Plugin.Log.LogInfo($"Ascension {level} curse choice: {string.Join(", ", offers.Select(o => $"{o.Curse.Name} ({o.Tier})"))}");
        }

        private CurseTier RollTier()
        {
            var roll = random.NextDouble();
            return roll < 0.45 ? CurseTier.Lesser : roll < 0.85 ? CurseTier.Normal : CurseTier.Greater;
        }

        private void OnChosen(int index)
        {
            var offer = offers[index];
            taken.Add(offer);
            pendingLevels.Dequeue();
            Plugin.Log.LogInfo($"Curse taken: {offer.Curse.Name} ({offer.Tier}), {taken.Count} in total");

            window.Destroy();
            window = null;
            encounterService.IsCurseChoiceOpen = false;
            openAt = Time.realtimeSinceStartup + 0.5f; //Another ascension's choice, if any

            if (synchronization.Value.IsPartyPauseEnabled())
            {
                synchronization.Value.RewardFinished(); //Waits for the others, the host releases everyone
                if (encounterService.IsWaiting)
                {
                    ScreenTextHelper.Show("Waiting for other player(s) choices...", new Vector2(0, -350));
                }
            }
        }

        /// <summary>
        /// Without choosing: the choice stays pending
        /// </summary>
        private void CloseWindow()
        {
            if (window == null)
            {
                return;
            }

            window.Destroy();
            window = null;
            encounterService.IsCurseChoiceOpen = false;
            if (MyTime.paused && !encounterService.IsWaiting)
            {
                MyTime.Unpause();
            }
        }

        public void Reset()
        {
            CloseWindow();
            taken.Clear();
            pendingLevels.Clear();
            isStageReady = false;
        }
    }
}
