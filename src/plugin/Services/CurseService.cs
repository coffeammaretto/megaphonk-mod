using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Utility;
using Megaphonk.Common.Messages.GameNetworkMessages;
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
    /// One stat a curse lowers (or raises, for a price) or a pact raises: an addition to the stat's multiplier, so a
    /// percentage of what the player has. A build without that stat doesn't care, which is the point of choosing.
    /// Flat effects (projectiles, bounces, jumps) add whole units instead.
    /// </summary>
    public record CurseEffect(EStat Stat, float Value, string Label, EStatModifyType Type = EStatModifyType.Addition)
    {
        public bool IsFlat => Type == EStatModifyType.Flat;

        /// <summary>
        /// Lower is better for these
        /// </summary>
        public bool IsGood => Value > 0f != (Stat == EStat.ChestPriceMultiplier);

        /// <summary>
        /// The value at a tier's scale: whole units, at least one, for flat effects
        /// </summary>
        public float Scaled(float scale)
        {
            return IsFlat ? Math.Sign(Value) * Math.Max(1f, MathF.Round(Math.Abs(Value * scale))) : Value * scale;
        }
    }

    /// <summary>
    /// A curse or a boon: stat effects, and a mechanic beyond stats (PerkService), a curse's at its tier's scale
    /// </summary>
    public record CurseDefinition(string Name, CurseEffect[] Effects, Perk Perk = Perk.None);

    public record CurseOffer(CurseDefinition Curse, CurseTier Tier)
    {
        public float Scale => CurseService.GetTierScale(Tier);
    }

    /// <summary>
    /// A card of the choice window: a curse, or a pact (a boon paid with a curse) when Boon is set
    /// </summary>
    public record ChoiceOffer(CurseOffer Curse, CurseDefinition Boon = null);

    public enum ChoiceKind
    {
        /// <summary>
        /// After an ascension, on the first stage
        /// </summary>
        AscensionCurse,

        /// <summary>
        /// A rift was failed
        /// </summary>
        RiftCurse,

        /// <summary>
        /// A rift was cleared
        /// </summary>
        RiftPact
    }

    /// <summary>
    /// Ascension curses and rift pacts: after each ascension or failed rift, every player picks one of three random
    /// curses for themselves (each character has its own weaknesses); after a cleared rift, one of three pacts (a boon
    /// paid with a lesser curse). They stack for the rest of the run, on the local player's stats only, so nothing goes
    /// over the network. While choosing, the party waits as for a level up (shared reward pause).
    /// </summary>
    public interface ICurseService
    {
        public IReadOnlyList<ChoiceOffer> Taken { get; }

        /// <summary>
        /// An ascension started: a choice once the next stage is loaded
        /// </summary>
        public void QueueChoice(int ascensionLevel);

        /// <summary>
        /// A rift ended: a pact when cleared, a curse when failed, as soon as possible on this stage
        /// </summary>
        public void QueueRiftChoice(bool cleared);

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
        private const float MAX_INCREASE = 2f;
        private const float MAX_FLAT = 3f; //Most projectiles, bounces or jumps added together

        // Stats are summed per stat, so a curse twice is twice the penalty. Some curses pay back with an upside.
        private static readonly CurseDefinition[] Curses =
        [
            new("Frailty", [new(EStat.MaxHealth, -0.25f, "Max HP")]),
            new("Weakness", [new(EStat.DamageMultiplier, -0.22f, "Damage")]),
            new("Lethargy", [new(EStat.AttackSpeed, -0.22f, "Attack speed")]),
            new("Burden", [new(EStat.MoveSpeedMultiplier, -0.18f, "Movement speed")]),
            new("Rot", [new(EStat.HealingMultiplier, -0.5f, "Healing"), new(EStat.HealthRegen, -0.6f, "HP regen")]),
            new("Myopia", [new(EStat.PickupRange, -0.5f, "Pickup range")]),
            new("Avarice", [new(EStat.GoldIncreaseMultiplier, -0.4f, "Gold"), new(EStat.SilverIncreaseMultiplier, -0.4f, "Silver")]),
            new("Misfortune", [new(EStat.Luck, -0.35f, "Luck")]),
            new("Exposure", [new(EStat.Armor, -0.45f, "Armor")]),
            new("Stiffness", [new(EStat.Evasion, -0.5f, "Evasion")]),
            new("Fading", [new(EStat.DurationMultiplier, -0.25f, "Duration"), new(EStat.EffectDurationMultiplier, -0.35f, "Effect duration")]),
            new("Withering", [new(EStat.SizeMultiplier, -0.25f, "Size")]),
            new("Dull Edge", [new(EStat.CritChance, -0.35f, "Crit chance"), new(EStat.CritDamage, -0.25f, "Crit damage")]),
            new("Cracked Shield", [new(EStat.Shield, -0.55f, "Shield")]),
            new("Bloodless", [new(EStat.Lifesteal, -0.6f, "Lifesteal")]),
            new("Blunted Thorns", [new(EStat.Thorns, -0.6f, "Thorns")]),
            new("Thick Air", [new(EStat.ProjectileSpeedMultiplier, -0.35f, "Projectile speed"), new(EStat.KnockbackMultiplier, -0.5f, "Knockback")]),
            new("Cowardice", [new(EStat.EliteDamageMultiplier, -0.4f, "Elite damage")]),
            new("Elemental Silence", [new(EStat.FireDamage, -0.35f, "Fire damage"), new(EStat.IceDamage, -0.35f, "Ice damage"), new(EStat.LightningDamage, -0.35f, "Lightning damage"), new(EStat.PoisonDamageMultiplier, -0.35f, "Poison damage")]),
            new("Cold Ashes", [new(EStat.BurnChance, -0.5f, "Burn chance"), new(EStat.FreezeChance, -0.5f, "Freeze chance")]),
            new("Dim Power", [new(EStat.PowerupChance, -0.5f, "Power-up chance"), new(EStat.PowerupBoostMultiplier, -0.35f, "Power-up strength")]),
            new("Inflation", [new(EStat.ChestPriceMultiplier, 0.35f, "Chest price")]),
            new("Grounded", [new(EStat.JumpHeight, -0.4f, "Jump height"), new(EStat.MoveSpeedMultiplier, -0.1f, "Movement speed")]),
            new("Glass Bones", [new(EStat.MaxHealth, -0.15f, "Max HP"), new(EStat.Armor, -0.25f, "Armor")]),
            new("Tremor", [new(EStat.DamageMultiplier, -0.12f, "Damage"), new(EStat.CritChance, -0.18f, "Crit chance")]),
            new("Exhaustion", [new(EStat.AttackSpeed, -0.12f, "Attack speed"), new(EStat.MoveSpeedMultiplier, -0.12f, "Movement speed")]),
            new("Dull Mind", [new(EStat.XpIncreaseMultiplier, -0.3f, "XP gain")]),
            new("Leaden Rounds", [new(EStat.ProjectileSpeedMultiplier, -0.35f, "Projectile speed"), new(EStat.AttackSpeed, -0.1f, "Attack speed")]),
            new("Scattershot", [new(EStat.Projectiles, 1f, "Projectiles", EStatModifyType.Flat), new(EStat.DamageMultiplier, -0.35f, "Damage")]),
            new("Reckless", [new(EStat.DamageMultiplier, 0.25f, "Damage"), new(EStat.MaxHealth, -0.35f, "Max HP")]),
            new("Bloodlust", [new(EStat.AttackSpeed, 0.2f, "Attack speed"), new(EStat.Armor, -0.5f, "Armor"), new(EStat.HealingMultiplier, -0.4f, "Healing")]),
            new("Giant", [new(EStat.SizeMultiplier, 0.3f, "Size"), new(EStat.MoveSpeedMultiplier, -0.2f, "Movement speed"), new(EStat.Evasion, -0.3f, "Evasion")]),
            new("Short Fuse", [new(EStat.AttackSpeed, 0.15f, "Attack speed"), new(EStat.DurationMultiplier, -0.35f, "Duration")]),
            new("Wild Bounce", [new(EStat.ProjectileBounces, 1f, "Projectile bounces", EStatModifyType.Flat), new(EStat.DamageMultiplier, -0.2f, "Damage"), new(EStat.ProjectileSpeedMultiplier, -0.2f, "Projectile speed")]),
            new("Recoil", [new(EStat.ProjectileSpeedMultiplier, 0.15f, "Projectile speed")], Perk.Recoil),
            new("Jammed", [], Perk.Misfire),
            new("Vengeful Dead", [], Perk.VengefulDead),
            new("Blood Price", [new(EStat.DamageMultiplier, 0.2f, "Damage")], Perk.BloodPrice),
            new("Short Range", [], Perk.ShortRange),
            new("Jinx", [], Perk.Unlucky),
        ];

        // Pact boons: worth more than the lesser or normal curse that comes with them
        private static readonly CurseDefinition[] Boons =
        [
            new("Might", [new(EStat.DamageMultiplier, 0.25f, "Damage")]),
            new("Haste", [new(EStat.AttackSpeed, 0.25f, "Attack speed")]),
            new("Vigor", [new(EStat.MaxHealth, 0.35f, "Max HP")]),
            new("Swiftness", [new(EStat.MoveSpeedMultiplier, 0.2f, "Movement speed")]),
            new("Precision", [new(EStat.CritChance, 0.4f, "Crit chance"), new(EStat.CritDamage, 0.3f, "Crit damage")]),
            new("Reach", [new(EStat.SizeMultiplier, 0.25f, "Size"), new(EStat.PickupRange, 0.5f, "Pickup range")]),
            new("Endurance", [new(EStat.DurationMultiplier, 0.35f, "Duration"), new(EStat.EffectDurationMultiplier, 0.35f, "Effect duration")]),
            new("Bulwark", [new(EStat.Armor, 0.5f, "Armor"), new(EStat.Shield, 0.5f, "Shield")]),
            new("Fortune", [new(EStat.Luck, 0.4f, "Luck"), new(EStat.GoldIncreaseMultiplier, 0.35f, "Gold")]),
            new("Leech", [new(EStat.Lifesteal, 0.6f, "Lifesteal"), new(EStat.HealingMultiplier, 0.5f, "Healing")]),
            new("Velocity", [new(EStat.ProjectileSpeedMultiplier, 0.4f, "Projectile speed"), new(EStat.KnockbackMultiplier, 0.5f, "Knockback")]),
            new("Elemental Fury", [new(EStat.FireDamage, 0.4f, "Fire damage"), new(EStat.IceDamage, 0.4f, "Ice damage"), new(EStat.LightningDamage, 0.4f, "Lightning damage"), new(EStat.PoisonDamageMultiplier, 0.4f, "Poison damage")]),
            new("Wisdom", [new(EStat.XpIncreaseMultiplier, 0.35f, "XP gain")]),
            new("Agility", [new(EStat.Evasion, 0.6f, "Evasion"), new(EStat.JumpHeight, 0.4f, "Jump height")]),
            new("Slayer", [new(EStat.EliteDamageMultiplier, 0.5f, "Elite damage")]),
            new("Multishot", [new(EStat.Projectiles, 1f, "Projectiles", EStatModifyType.Flat)]),
            new("Ricochet", [new(EStat.ProjectileBounces, 2f, "Projectile bounces", EStatModifyType.Flat), new(EStat.ProjectileSpeedMultiplier, 0.2f, "Projectile speed")]),
            new("Skywalker", [new(EStat.ExtraJumps, 1f, "Extra jumps", EStatModifyType.Flat), new(EStat.MoveSpeedMultiplier, 0.1f, "Movement speed")]),
            new("Inferno", [new(EStat.BurnChance, 0.5f, "Burn chance"), new(EStat.FreezeChance, 0.5f, "Freeze chance")]),
            new("Empowered", [new(EStat.PowerupChance, 0.6f, "Power-up chance"), new(EStat.PowerupBoostMultiplier, 0.4f, "Power-up strength")]),
            new("Juggernaut", [new(EStat.Thorns, 0.8f, "Thorns"), new(EStat.MaxHealth, 0.15f, "Max HP")]),
            new("Barrage", [new(EStat.Projectiles, 1f, "Projectiles", EStatModifyType.Flat), new(EStat.ProjectileBounces, 1f, "Projectile bounces", EStatModifyType.Flat)]),
            new("Blast Rounds", [], Perk.Explosive),
            new("Splinter", [], Perk.Splitting),
            new("Storm Caller", [], Perk.ChainLightning),
            new("Lance", [], Perk.Piercing),
            new("Reaper", [], Perk.Execute),
            new("Hunter's Mark", [], Perk.Mark),
            new("Echo", [], Perk.Echo),
            new("Plague", [], Perk.PoisonCloud),
        ];

        private readonly IEncounterService encounterService;
        // Resolved when used: the synchronization service depends on services that depend on this one
        private readonly Lazy<ISynchronizationService> synchronization = new(() => Plugin.Services.GetService<ISynchronizationService>());
        private readonly System.Random random = new();
        private readonly List<ChoiceOffer> taken = [];
        private readonly Queue<(ChoiceKind Kind, int Level)> pending = new();
        private CurseChoiceWindow window;
        private List<ChoiceOffer> offers;
        private bool isStageReady;
        private float openAt;

        public IReadOnlyList<ChoiceOffer> Taken => taken;

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
            pending.Enqueue((ChoiceKind.AscensionCurse, ascensionLevel));
            isStageReady = false; //Not on the stage being left: once the next one started
        }

        public void QueueRiftChoice(bool cleared)
        {
            pending.Enqueue((cleared ? ChoiceKind.RiftPact : ChoiceKind.RiftCurse, 0));
            openAt = Mathf.Min(openAt, Time.realtimeSinceStartup + 0.5f);
        }

        public void AddModifiers(Dictionary<string, (EStat Stat, EStatModifyType Type, float Value)> wanted)
        {
            var totals = new Dictionary<(EStat Stat, EStatModifyType Type), float>();
            foreach (var offer in taken)
            {
                foreach (var effect in offer.Curse.Curse.Effects)
                {
                    totals[(effect.Stat, effect.Type)] = totals.GetValueOrDefault((effect.Stat, effect.Type)) + effect.Scaled(offer.Curse.Scale);
                }

                foreach (var effect in offer.Boon?.Effects ?? [])
                {
                    totals[(effect.Stat, effect.Type)] = totals.GetValueOrDefault((effect.Stat, effect.Type)) + effect.Value;
                }
            }

            foreach (var ((stat, type), total) in totals)
            {
                wanted[type == EStatModifyType.Flat ? "CurseFlat" + stat : "Curse" + stat] = type == EStatModifyType.Flat
                    ? (stat, type, Mathf.Clamp(total, -MAX_FLAT, MAX_FLAT))
                    : (stat, type, Mathf.Clamp(total, -MAX_PENALTY, MAX_INCREASE));
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

            if (pending.Count == 0 || !isStageReady || Time.realtimeSinceStartup < openAt)
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

            Open(pending.Peek());
        }

        private void Open((ChoiceKind Kind, int Level) choice)
        {
            offers = choice.Kind == ChoiceKind.RiftPact
                ? RollPacts()
                : Curses.OrderBy(_ => random.Next()).Take(OFFER_COUNT).Select(curse => new ChoiceOffer(new CurseOffer(curse, RollTier()))).ToList();

            var (title, subtitle) = choice.Kind switch
            {
                ChoiceKind.RiftPact => (Loc.T("Rift cleared"), Loc.T("Choose a pact: its boon and its curse stay for the rest of the run")),
                ChoiceKind.RiftCurse => (Loc.T("Rift failed"), Loc.T("Choose a curse: it stays for the rest of the run")),
                _ => (Loc.F("Ascension {0}", choice.Level), Loc.T("Choose a curse: it stays for the rest of the run"))
            };
            window = CurseChoiceWindow.Open(title, subtitle, offers, taken, OnChosen);
            if (window == null)
            {
                openAt = Time.realtimeSinceStartup + OPEN_DELAY; //No UI yet: try again
                return;
            }

            encounterService.IsCurseChoiceOpen = true;
            if (synchronization.Value.IsPartyPauseEnabled() || IsSolo)
            {
                MyTime.Pause(); //The host pauses the others when it sees this player choosing; alone, a plain pause
            }
            Plugin.Log.LogInfo($"{choice.Kind} choice: {string.Join(", ", offers.Select(Describe))}");
        }

        private static string Describe(ChoiceOffer offer)
        {
            return $"{(offer.Boon == null ? string.Empty : offer.Boon.Name + " + ")}{offer.Curse.Curse.Name} ({offer.Curse.Tier})";
        }

        /// <summary>
        /// Pairs each boon with a curse that touches none of its stats, so a pact never raises and lowers the same stat
        /// </summary>
        private List<ChoiceOffer> RollPacts()
        {
            var curses = Curses.OrderBy(_ => random.Next()).ToList();
            var pacts = new List<ChoiceOffer>();
            foreach (var boon in Boons.OrderBy(_ => random.Next()))
            {
                var stats = boon.Effects.Select(effect => effect.Stat).ToHashSet();
                var curse = curses.FirstOrDefault(c => c.Effects.All(effect => !stats.Contains(effect.Stat)));
                if (curse == null)
                {
                    continue;
                }

                curses.Remove(curse);
                pacts.Add(new ChoiceOffer(new CurseOffer(curse, RollPactTier()), boon));
                if (pacts.Count == OFFER_COUNT)
                {
                    break;
                }
            }
            return pacts;
        }

        private CurseTier RollPactTier()
        {
            return random.NextDouble() < 0.6 ? CurseTier.Lesser : CurseTier.Normal;
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
            pending.Dequeue();
            Plugin.Log.LogInfo($"Taken: {Describe(offer)}, {taken.Count} in total");

            window.Destroy();
            window = null;
            encounterService.IsCurseChoiceOpen = false;
            openAt = Time.realtimeSinceStartup + 0.5f; //Another choice, if any

            if (synchronization.Value.IsPartyPauseEnabled())
            {
                synchronization.Value.RewardFinished(); //Waits for the others, the host releases everyone
                if (encounterService.IsWaiting)
                {
                    ScreenTextHelper.Show("Waiting for other player(s) choices...", new Vector2(0, -350));
                }
            }
            else if (IsSolo && MyTime.paused)
            {
                MyTime.Unpause();
            }
        }

        private static bool IsSolo => Plugin.Services.GetService<IUdpClientService>()?.IsSoloSession() ?? false;

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
            pending.Clear();
            isStageReady = false;
        }
    }
}
