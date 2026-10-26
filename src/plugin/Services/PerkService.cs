using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Utility;
using Megaphonk.Common.Messages.GameNetworkMessages;
using Megaphonk.Extensions;
using Megaphonk.Helpers;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Megaphonk.Services
{
    /// <summary>
    /// Pact and curse mechanics beyond stats (Patches/Perks.cs hooks them). Hit perks (explosion, shards, chain lightning,
    /// piercing, execute, mark, bad luck, poison clouds) run on the host, which simulates every player's hits: their damage
    /// reaches the clients like any enemy damage, their visuals with PerkFx. Each client sends the host its perks. Shot
    /// perks (echo, misfire, recoil, blood price, short range) and the vengeful dead run on the owner's machine.
    /// </summary>
    public interface IPerkService
    {
        public bool IsHost { get; }
        public uint? LocalId { get; }

        /// <summary>
        /// A perk's strength for a player (the sum of the taken cards with it, a curse at its tier's scale), 0 without it
        /// </summary>
        public float GetStrength(uint? ownerId, Perk perk);

        /// <summary>
        /// Host: a player's weapon hit an enemy for this damage (after the hit perks that change it)
        /// </summary>
        public void OnWeaponHit(Enemy enemy, float damage, Vector3 direction, uint ownerId);

        /// <summary>
        /// Host: the damage of a weapon hit, changed by the mark and bad luck
        /// </summary>
        public float ModifyHitDamage(Enemy enemy, float damage, uint ownerId);

        public void OnEnemyDied(Enemy enemy, uint? killerId);
        public void QueueEcho(WeaponBase weapon);
        public void Tick();
        public void Reset();
    }

    internal class PerkService : IPerkService
    {
        private const string DAMAGE_SOURCE = "Megaphonk perk";
        private const int MAX_PROCS_PER_FRAME = 40; //Hit perks over this in one frame are skipped: hundreds of hits a second
        private const int MAX_FX_PER_SECOND = 30;
        private const float RESEND_INTERVAL = 5f;
        private const float MAX_STRENGTH = 20f; //Sent by clients: bounded

        private const float EXPLOSION_RADIUS = 3.5f;
        private const float EXPLOSION_SHARE = 0.35f;
        private const int EXPLOSION_MAX_TARGETS = 8;
        private const float SHARD_RANGE = 8f;
        private const float SHARD_SHARE = 0.5f;
        private const float CHAIN_CHANCE = 0.2f;
        private const float CHAIN_RANGE = 8f;
        private const float CHAIN_SHARE = 0.6f;
        private const int CHAIN_JUMPS = 3;
        private const float PIERCE_RANGE = 7f;
        private const float PIERCE_WIDTH = 1.5f;
        private const float PIERCE_SHARE = 0.6f;
        private const float EXECUTE_SHARE = 0.08f;
        private const float EXECUTE_MAX = 0.25f;
        private const float MARK_BONUS = 0.25f;
        private const float ECHO_CHANCE = 0.15f;
        private const float ECHO_DELAY = 0.3f;
        private const float CLOUD_CHANCE = 0.25f;
        private const float CLOUD_RADIUS = 4f;
        private const float CLOUD_DURATION = 3f;
        private const float CLOUD_DPS_SHARE = 0.2f; //Of the dead enemy's max HP per second
        private const int MAX_CLOUDS = 10;
        private const float UNLUCKY_CHANCE = 0.15f;
        private const float VENGEFUL_RADIUS = 4f;
        private const float VENGEFUL_SHARE = 0.03f;
        private const float VENGEFUL_COOLDOWN = 0.5f;

        private readonly IUdpClientService udpClientService;
        private readonly IPlayerManagerService playerManagerService;
        private readonly IEnemyManagerService enemyManagerService;
        private readonly ICurseService curseService;
        private readonly System.Random random = new();

        private readonly Dictionary<Perk, float> localPerks = [];
        private readonly Dictionary<uint, Dictionary<Perk, float>> remotePerks = [];
        private readonly HashSet<(IntPtr Enemy, uint Owner)> marked = [];
        private readonly List<(WeaponBase Weapon, float At)> echoes = [];
        private readonly List<Cloud> clouds = [];
        private readonly List<(GameObject Root, LineRenderer Line, float Start, float End, PerkFx Fx)> visuals = [];
        private readonly List<(Enemy Enemy, Vector3 Position)> enemies = [];
        private int takenCount = -1;
        private float nextSendAt;
        private int procFrame = -1;
        private int procsThisFrame;
        private int enemiesFrame = -1;
        private float fxWindowStart;
        private int fxInWindow;
        private float nextVengefulAt;
        private bool applying;
        private Material lineMaterial;

        private class Cloud
        {
            public Vector3 Position;
            public float EndAt;
            public float NextTickAt;
            public float DamagePerTick;
            public uint Owner;
        }

        /// <summary>
        /// A perk's line on a card, at a strength
        /// </summary>
        public static string DescribePerk(Perk perk, float strength)
        {
            static string N(float value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return perk switch
            {
                Perk.Explosive => Loc.F("Hits explode: {0}% of the damage within 3.5 m", N(EXPLOSION_SHARE * 100f * strength)),
                Perk.Splitting => Loc.F("Hits split into {0} shards of 50% damage", Math.Max(1, (int)Math.Round(2 * strength))),
                Perk.ChainLightning => Loc.F("{0}% chance of chain lightning: 3 jumps of 60% damage", N(CHAIN_CHANCE * 100f * strength)),
                Perk.Piercing => Loc.F("Hits pierce up to {0} enemies behind for 60% damage", Math.Max(1, (int)Math.Round(2 * strength))),
                Perk.Execute => Loc.F("Hits kill non-boss enemies under {0}% HP", N(Math.Min(EXECUTE_SHARE * strength, EXECUTE_MAX) * 100f)),
                Perk.Mark => Loc.F("Repeated hits on an enemy deal +{0}% damage", N(MARK_BONUS * 100f * strength)),
                Perk.Echo => Loc.F("{0}% chance a weapon attacks again", N(Math.Min(ECHO_CHANCE * strength, 0.6f) * 100f)),
                Perk.PoisonCloud => Loc.F("{0}% chance a kill leaves a poison cloud for 3 s", N(CLOUD_CHANCE * 100f * strength)),
                Perk.Recoil => Loc.T("Your shots push you back"),
                Perk.Misfire => Loc.F("{0}% of attacks misfire", N(Math.Min(0.1f * strength, 0.5f) * 100f)),
                Perk.VengefulDead => Loc.F("Enemies dying within 4 m hurt you for {0}% max HP", N(VENGEFUL_SHARE * 100f * strength)),
                Perk.BloodPrice => Loc.F("Each attack costs {0}% max HP (never kills)", N(0.5f * strength)),
                Perk.ShortRange => Loc.F("Projectiles last {0}% shorter", N(Math.Min(0.3f * strength, 0.7f) * 100f)),
                Perk.Unlucky => Loc.F("{0}% of hits deal half damage", N(UNLUCKY_CHANCE * 100f * strength)),
                _ => string.Empty
            };
        }

        public static bool IsGood(Perk perk)
        {
            return perk < Perk.Recoil;
        }

        public bool IsHost => udpClientService.IsHost() ?? false;
        public uint? LocalId => playerManagerService.GetLocalPlayer()?.ConnectionId;

        public PerkService(IUdpClientService udpClientService, IPlayerManagerService playerManagerService, IEnemyManagerService enemyManagerService, ICurseService curseService)
        {
            this.udpClientService = udpClientService;
            this.playerManagerService = playerManagerService;
            this.enemyManagerService = enemyManagerService;
            this.curseService = curseService;

            EventManager.SubscribePlayerPerksEvents(perks =>
            {
                if (!IsHost)
                {
                    return;
                }

                var set = new Dictionary<Perk, float>();
                for (var i = 0; i < perks.Perks.Length && i < perks.Strengths.Length && i < 32; i++)
                {
                    var strength = perks.Strengths[i];
                    if (Enum.IsDefined(perks.Perks[i]) && strength > 0f && strength <= MAX_STRENGTH)
                    {
                        set[perks.Perks[i]] = strength;
                    }
                }

                if (!remotePerks.TryGetValue(perks.OwnerId, out var known) || !known.OrderBy(p => p.Key).SequenceEqual(set.OrderBy(p => p.Key)))
                {
                    Plugin.Log.LogInfo($"Perks of player {perks.OwnerId}: {Describe(set)}");
                }
                remotePerks[perks.OwnerId] = set;
            });
            EventManager.SubscribePerkFxEvents(ShowFx);
        }

        public float GetStrength(uint? ownerId, Perk perk)
        {
            if (!ownerId.HasValue)
            {
                return 0f;
            }

            var set = ownerId == LocalId ? localPerks : remotePerks.GetValueOrDefault(ownerId.Value);
            return set != null && set.TryGetValue(perk, out var strength) ? strength : 0f;
        }

        private bool TryProc()
        {
            if (Time.frameCount != procFrame)
            {
                procFrame = Time.frameCount;
                procsThisFrame = 0;
            }

            return !applying && procsThisFrame++ < MAX_PROCS_PER_FRAME;
        }

        public float ModifyHitDamage(Enemy enemy, float damage, uint ownerId)
        {
            var mark = GetStrength(ownerId, Perk.Mark);
            if (mark > 0f && !marked.Add((enemy.Ptr(), ownerId)))
            {
                damage *= 1f + MARK_BONUS * mark;
            }

            var unlucky = GetStrength(ownerId, Perk.Unlucky);
            if (unlucky > 0f && random.NextDouble() < UNLUCKY_CHANCE * unlucky)
            {
                damage *= 0.5f;
            }

            if (marked.Count > 4096)
            {
                marked.Clear(); //Dead enemies' marks: bounded
            }
            return damage;
        }

        public void OnWeaponHit(Enemy enemy, float damage, Vector3 direction, uint ownerId)
        {
            if (damage <= 0f || !TryProc())
            {
                return;
            }

            var position = enemy.transform.position;

            var execute = GetStrength(ownerId, Perk.Execute);
            if (execute > 0f && !enemy.IsDeadOrDyingNextFrame() && !IsBoss(enemy) && enemy.GetHpRatio() < Math.Min(EXECUTE_SHARE * execute, EXECUTE_MAX))
            {
                Deal(enemy, enemy.hp * 2f + 10f, direction, ownerId); //Past any armor
                SendFx(new PerkFx { Kind = PerkFxKind.Explosion, Position = position.ToNumericsVector3(), Radius = 1.2f, Duration = 0.25f });
            }

            var explosive = GetStrength(ownerId, Perk.Explosive);
            if (explosive > 0f)
            {
                foreach (var (target, _) in Nearest(position, EXPLOSION_RADIUS, EXPLOSION_MAX_TARGETS, enemy))
                {
                    Deal(target, damage * EXPLOSION_SHARE * explosive, direction, ownerId);
                }
                SendFx(new PerkFx { Kind = PerkFxKind.Explosion, Position = position.ToNumericsVector3(), Radius = EXPLOSION_RADIUS, Duration = 0.3f });
            }

            var splitting = GetStrength(ownerId, Perk.Splitting);
            if (splitting > 0f)
            {
                var targets = Nearest(position, SHARD_RANGE, Math.Max(1, (int)Math.Round(2 * splitting)), enemy);
                foreach (var (target, _) in targets)
                {
                    Deal(target, damage * SHARD_SHARE, direction, ownerId);
                }
                SendFx(new PerkFx { Kind = PerkFxKind.Shards, Position = position.ToNumericsVector3(), Duration = 0.2f, Targets = targets.Select(t => t.Position.ToNumericsVector3()).ToArray() });
            }

            var chain = GetStrength(ownerId, Perk.ChainLightning);
            if (chain > 0f && random.NextDouble() < CHAIN_CHANCE * chain)
            {
                var hit = new List<Enemy> { enemy };
                var points = new List<Vector3>();
                var from = position;
                for (var i = 0; i < CHAIN_JUMPS; i++)
                {
                    var next = Nearest(from, CHAIN_RANGE, 1, hit.ToArray());
                    if (next.Count == 0)
                    {
                        break;
                    }

                    Deal(next[0].Enemy, damage * CHAIN_SHARE, direction, ownerId);
                    hit.Add(next[0].Enemy);
                    points.Add(next[0].Position);
                    from = next[0].Position;
                }

                if (points.Count > 0)
                {
                    SendFx(new PerkFx { Kind = PerkFxKind.Lightning, Position = position.ToNumericsVector3(), Duration = 0.25f, Targets = points.Select(p => p.ToNumericsVector3()).ToArray() });
                }
            }

            var piercing = GetStrength(ownerId, Perk.Piercing);
            if (piercing > 0f)
            {
                var forward = new Vector3(direction.x, 0f, direction.z);
                if (forward.sqrMagnitude > 0.01f)
                {
                    forward.Normalize();
                    var behind = Nearest(position, PIERCE_RANGE, Math.Max(1, (int)Math.Round(2 * piercing)), enemy, candidate =>
                    {
                        var offset = candidate - position;
                        offset.y = 0f;
                        var along = Vector3.Dot(offset, forward);
                        return along > 0f && (offset - forward * along).magnitude < PIERCE_WIDTH;
                    });
                    foreach (var (target, _) in behind)
                    {
                        Deal(target, damage * PIERCE_SHARE, direction, ownerId);
                    }

                    if (behind.Count > 0)
                    {
                        SendFx(new PerkFx { Kind = PerkFxKind.Shards, Position = position.ToNumericsVector3(), Duration = 0.2f, Targets = [behind[^1].Position.ToNumericsVector3()] });
                    }
                }
            }
        }

        private static bool IsBoss(Enemy enemy)
        {
            return enemy.IsBoss() || enemy.IsStageBoss() || enemy.IsFinalBoss();
        }

        /// <summary>
        /// Perk damage, sent to the clients by the enemy damage patch like any other. The kill goes to the owner.
        /// </summary>
        private void Deal(Enemy enemy, float damage, Vector3 direction, uint ownerId)
        {
            if (enemy == null || damage <= 0f || enemy.IsDeadOrDyingNextFrame())
            {
                return;
            }

            applying = true;
            try
            {
                var dc = new DamageContainer(0f, DAMAGE_SOURCE)
                {
                    damage = damage,
                    direction = direction
                };
                DynamicData.For(dc).Set("ownerId", ownerId);
                enemy.Damage(dc);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Perk damage failed: {ex.Message}");
            }
            finally
            {
                applying = false;
            }
        }

        /// <summary>
        /// Living enemies around a point, nearest first. Positions are read once per frame.
        /// </summary>
        private List<(Enemy Enemy, Vector3 Position)> Nearest(Vector3 center, float range, int count, Enemy exclude, Func<Vector3, bool> filter = null)
        {
            return Nearest(center, range, count, [exclude], filter);
        }

        private List<(Enemy Enemy, Vector3 Position)> Nearest(Vector3 center, float range, int count, Enemy[] exclude, Func<Vector3, bool> filter = null)
        {
            RefreshEnemies();
            var excluded = exclude.Where(e => e != null).Select(e => e.Ptr()).ToHashSet();
            var squared = range * range;
            return enemies
                .Where(e => !excluded.Contains(e.Enemy.Ptr()) && (e.Position - center).sqrMagnitude <= squared && (filter == null || filter(e.Position)))
                .OrderBy(e => (e.Position - center).sqrMagnitude)
                .Where(e => e.Enemy != null && e.Enemy.CachedPtr() != IntPtr.Zero && !e.Enemy.IsDeadOrDyingNextFrame())
                .Take(count)
                .ToList();
        }

        private void RefreshEnemies()
        {
            if (enemiesFrame == Time.frameCount)
            {
                return;
            }

            enemiesFrame = Time.frameCount;
            enemies.Clear();
            foreach (var enemy in enemyManagerService.GetAllSpawnedEnemies())
            {
                try
                {
                    if (enemy != null && enemy.CachedPtr() != IntPtr.Zero)
                    {
                        enemies.Add((enemy, enemy.transform.position));
                    }
                }
                catch
                {
                    //Destroyed meanwhile
                }
            }
        }

        public void OnEnemyDied(Enemy enemy, uint? killerId)
        {
            Vector3 position;
            try
            {
                position = enemy.transform.position;
                if (marked.Count > 0)
                {
                    var pointer = enemy.Ptr();
                    marked.RemoveWhere(m => m.Enemy == pointer); //Enemies are pooled: a new one starts unmarked
                }
            }
            catch
            {
                return;
            }

            // The local player's curse: on every machine, for its own player
            var vengeful = GetStrength(LocalId, Perk.VengefulDead);
            if (vengeful > 0f && Time.time >= nextVengefulAt)
            {
                var player = GameManager.Instance?.player;
                var health = player?.inventory?.playerHealth;
                if (health != null && (player.transform.position - position).sqrMagnitude < VENGEFUL_RADIUS * VENGEFUL_RADIUS)
                {
                    nextVengefulAt = Time.time + VENGEFUL_COOLDOWN;
                    health.DamagePlayerExternal(health.GetCombinedMaxHp() * VENGEFUL_SHARE * vengeful, 0f, Vector3.zero, false, "Megaphonk vengeful dead", default(DcFlags), default(EDamageEffect), null);
                    ShowFx(new PerkFx { Kind = PerkFxKind.Explosion, Position = position.ToNumericsVector3(), Radius = VENGEFUL_RADIUS, Duration = 0.3f });
                }
            }

            var cloud = GetStrength(killerId, Perk.PoisonCloud);
            if (IsHost && cloud > 0f && clouds.Count < MAX_CLOUDS && random.NextDouble() < CLOUD_CHANCE * cloud)
            {
                clouds.Add(new Cloud
                {
                    Position = position,
                    EndAt = Time.time + CLOUD_DURATION,
                    NextTickAt = Time.time + 0.5f,
                    DamagePerTick = enemy.maxHp * CLOUD_DPS_SHARE * 0.5f,
                    Owner = killerId.Value
                });
                SendFx(new PerkFx { Kind = PerkFxKind.PoisonCloud, Position = position.ToNumericsVector3(), Radius = CLOUD_RADIUS, Duration = CLOUD_DURATION });
            }
        }

        public void QueueEcho(WeaponBase weapon)
        {
            var echo = GetStrength(LocalId, Perk.Echo);
            if (echo > 0f && echoes.Count < 16 && random.NextDouble() < Math.Min(ECHO_CHANCE * echo, 0.6f))
            {
                echoes.Add((weapon, MyTime.time + ECHO_DELAY));
            }
        }

        public void Tick()
        {
            SyncLocalPerks();
            TickEchoes();
            TickClouds();
            TickVisuals();
        }

        /// <summary>
        /// The local perks from the taken pacts and curses, sent to the host on a change and now and then (a late join)
        /// </summary>
        private void SyncLocalPerks()
        {
            var taken = curseService.Taken;
            if (taken.Count != takenCount)
            {
                takenCount = taken.Count;
                localPerks.Clear();
                foreach (var offer in taken)
                {
                    if (offer.Boon != null && offer.Boon.Perk != Perk.None)
                    {
                        localPerks[offer.Boon.Perk] = localPerks.GetValueOrDefault(offer.Boon.Perk) + 1f;
                    }

                    if (offer.Curse.Curse.Perk != Perk.None)
                    {
                        localPerks[offer.Curse.Curse.Perk] = localPerks.GetValueOrDefault(offer.Curse.Curse.Perk) + offer.Curse.Scale;
                    }
                }

                Plugin.Log.LogInfo($"Local perks: {Describe(localPerks)}");
                nextSendAt = 0f;
            }

            if (IsHost || Time.realtimeSinceStartup < nextSendAt || LocalId is not uint localId)
            {
                return;
            }

            nextSendAt = Time.realtimeSinceStartup + RESEND_INTERVAL;
            udpClientService.SendToHost(new PlayerPerks
            {
                OwnerId = localId,
                Perks = [.. localPerks.Keys],
                Strengths = [.. localPerks.Values]
            }, LiteNetLib.DeliveryMethod.ReliableOrdered);
        }

        private static string Describe(Dictionary<Perk, float> perks)
        {
            return perks.Count == 0 ? "none" : string.Join(", ", perks.Select(p => $"{p.Key} x{p.Value:0.##}"));
        }

        private void TickEchoes()
        {
            for (var i = echoes.Count - 1; i >= 0; i--)
            {
                var (weapon, at) = echoes[i];
                if (MyTime.time < at)
                {
                    continue;
                }

                echoes.RemoveAt(i);
                try
                {
                    var health = GameManager.Instance?.player?.inventory?.playerHealth;
                    if (weapon != null && weapon.enabled && health != null && health.hp > 0)
                    {
                        WeaponUtility.WeaponAttack(weapon);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Echo failed: {ex.Message}");
                }
            }
        }

        private void TickClouds()
        {
            if (!IsHost)
            {
                clouds.Clear();
                return;
            }

            for (var i = clouds.Count - 1; i >= 0; i--)
            {
                var cloud = clouds[i];
                if (Time.time >= cloud.EndAt)
                {
                    clouds.RemoveAt(i);
                    continue;
                }

                if (Time.time < cloud.NextTickAt)
                {
                    continue;
                }

                cloud.NextTickAt = Time.time + 0.5f;
                foreach (var (target, _) in Nearest(cloud.Position, CLOUD_RADIUS, EXPLOSION_MAX_TARGETS * 2, Array.Empty<Enemy>()))
                {
                    Deal(target, cloud.DamagePerTick, Vector3.zero, cloud.Owner);
                }
            }
        }

        /// <summary>
        /// Host: shown here and on the clients, a few dozen a second at most (only visuals are dropped)
        /// </summary>
        private void SendFx(PerkFx fx)
        {
            if (Time.realtimeSinceStartup - fxWindowStart > 1f)
            {
                fxWindowStart = Time.realtimeSinceStartup;
                fxInWindow = 0;
            }

            if (++fxInWindow > MAX_FX_PER_SECOND)
            {
                return;
            }

            ShowFx(fx);
            if (IsHost)
            {
                udpClientService.SendToAllClients(fx, LiteNetLib.DeliveryMethod.Unreliable);
            }
        }

        private void ShowFx(PerkFx fx)
        {
            if (visuals.Count >= 64 || fx.Targets.Length > 16)
            {
                return; //Received ones aren't rate limited here
            }

            try
            {
                var root = new GameObject("Megaphonk_PerkFx");
                var line = root.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                var material = GetLineMaterial();
                if (material != null)
                {
                    line.material = material;
                }

                var position = fx.Position.ToUnityVector3();
                switch (fx.Kind)
                {
                    case PerkFxKind.Shards:
                    case PerkFxKind.Lightning:
                        var points = new List<Vector3> { position + Vector3.up };
                        foreach (var target in fx.Targets)
                        {
                            var end = target.ToUnityVector3() + Vector3.up;
                            if (fx.Kind == PerkFxKind.Lightning)
                            {
                                var mid = (points[^1] + end) / 2f + new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble(), (float)random.NextDouble() - 0.5f) * 1.5f;
                                points.Add(mid);
                            }
                            else if (points.Count > 1)
                            {
                                points.Add(position + Vector3.up); //Each shard from the hit
                            }
                            points.Add(end);
                        }
                        line.positionCount = points.Count;
                        line.SetPositions(points.ToArray());
                        line.startWidth = fx.Kind == PerkFxKind.Lightning ? 0.35f : 0.2f;
                        line.endWidth = line.startWidth;
                        break;
                    default:
                        line.loop = true;
                        line.positionCount = 24;
                        line.startWidth = fx.Kind == PerkFxKind.PoisonCloud ? 0.5f : 0.35f;
                        line.endWidth = line.startWidth;
                        break;
                }

                visuals.Add((root, line, Time.time, Time.time + Math.Max(0.1f, fx.Duration), fx));
                UpdateVisual(line, fx, 0f);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Perk visual failed: {ex.Message}");
            }
        }

        private void TickVisuals()
        {
            for (var i = visuals.Count - 1; i >= 0; i--)
            {
                var (root, line, start, end, fx) = visuals[i];
                if (root == null || Time.time >= end)
                {
                    if (root != null)
                    {
                        UnityEngine.Object.Destroy(root);
                    }
                    visuals.RemoveAt(i);
                    continue;
                }

                UpdateVisual(line, fx, (Time.time - start) / (end - start));
            }
        }

        private static void UpdateVisual(LineRenderer line, PerkFx fx, float t)
        {
            var color = fx.Kind switch
            {
                PerkFxKind.Explosion => new Color(1f, 0.55f, 0.15f),
                PerkFxKind.Lightning => new Color(0.7f, 0.85f, 1f),
                PerkFxKind.PoisonCloud => new Color(0.45f, 1f, 0.3f),
                _ => new Color(1f, 1f, 0.8f)
            };
            color.a = fx.Kind == PerkFxKind.PoisonCloud ? 0.8f * (1f - t * t) : 1f - t;
            line.startColor = color;
            line.endColor = color;

            if (fx.Kind is PerkFxKind.Explosion or PerkFxKind.PoisonCloud)
            {
                var center = fx.Position.ToUnityVector3();
                var radius = fx.Kind == PerkFxKind.Explosion
                    ? fx.Radius * (0.3f + 0.7f * t)
                    : fx.Radius * (0.85f + 0.15f * (float)Math.Sin(t * Math.PI * 6));
                for (var i = 0; i < line.positionCount; i++)
                {
                    var angle = i * Math.PI * 2 / line.positionCount;
                    line.SetPosition(i, center + new Vector3((float)Math.Cos(angle) * radius, 0.5f, (float)Math.Sin(angle) * radius));
                }
            }
        }

        private Material GetLineMaterial()
        {
            if (lineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                if (shader != null)
                {
                    lineMaterial = new Material(shader);
                }
            }

            return lineMaterial;
        }

        /// <summary>
        /// Clients' perks are kept: they follow their own taken cards and are resent on a change
        /// </summary>
        public void Reset()
        {
            marked.Clear();
            echoes.Clear();
            clouds.Clear();
            foreach (var (root, _, _, _, _) in visuals)
            {
                if (root != null)
                {
                    UnityEngine.Object.Destroy(root);
                }
            }
            visuals.Clear();
            takenCount = -1;
        }
    }
}
