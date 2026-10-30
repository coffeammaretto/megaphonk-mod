using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Camera;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Managers;
using Assets.Scripts.Utility;
using Megaphonk.Common.Messages.GameNetworkMessages;
using Megaphonk.Extensions;
using Megaphonk.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using EEnemy = global::Actors.Enemies.EEnemy;

namespace Megaphonk.Services
{
    /// <summary>
    /// Rifts, the co-op endgame's objectives: from the first ascension, every stage but the final boss's opens one now
    /// and then, a random kind: zones to hold (each fills while players, alive and not downed, stand in it, faster with
    /// more of them), shards to collect (scattered over the map, picked up by walking to them), both before the time runs
    /// out, or a storm to survive (a shrinking circle, outside of which each player loses a share of their HP every
    /// second, applied by their own machine). Cleared, every player picks a pact; failed, a curse (CurseService). A rift
    /// adds no enemy: the stage's own ones are the threat. The host runs it and sends its progress; every machine shows it.
    /// </summary>
    public interface IRiftService
    {
        public int ClearedCount { get; }
        public void Tick();
        public void Reset();
    }

    internal class RiftService : IRiftService
    {
        private const float FIRST_RIFT_DELAY = 60f;
        private const float RIFT_INTERVAL = 180f;
        private const float DURATION = 120f; //Zones can be anywhere on the map
        private const float HOLD_SECONDS = 20f;
        private const float ZONE_RADIUS = 9f;
        private const float ZONE_HEIGHT_TOLERANCE = 8f;
        private const float MIN_ZONE_DISTANCE = 20f;
        private const float MAX_ZONE_DISTANCE = 35f;
        private const int MAP_ATTEMPTS = 120;
        private const int CIRCLE_CHECKS = 12;
        private const float MAX_GROUND_STEP = 2.5f; //Height difference allowed under the circle
        private const float SKY_HEIGHT = 300f;
        private const int MAX_ZONES = 2;
        private const float PROGRESS_SEND_INTERVAL = 0.25f;
        private const float RESULT_SHOWN_SECONDS = 4f;
        private const int RING_SEGMENTS = 48;
        private const float BEAM_HEIGHT = 200f;
        private const float STORM_RING_STEP = 0.1f; //The shrinking storm circle is redrawn by steps this big
        private const float SHARD_PICKUP_RADIUS = 3f;
        private const float SHARD_CLEAR_RADIUS = 2f; //Open ground needed around a shard
        private const float SHARD_SPACING = 25f;
        private const int SHARDS_PER_PLAYER = 3;
        private const int MIN_SHARDS = 4;
        private const int MAX_SHARDS = 8;
        private const float STORM_DURATION = 90f;
        private const float STORM_SHRINK_SECONDS = 60f; //Then the circle stays at its smallest until the end
        private const float STORM_END_RADIUS = 12f;
        private const float STORM_MIN_START_RADIUS = 60f;
        private const float STORM_START_MARGIN = 25f; //Everyone starts well inside
        private const float STORM_DAMAGE_INTERVAL = 1f;
        private const float STORM_DAMAGE_SHARE = 0.08f; //Of the max HP, each interval outside
        private const float ELITE_DURATION = 90f;
        private const int MIN_ELITES = 2;
        private const int MAX_ELITES = 5;
        private const float ELITE_SIZE = 1.25f;
        private const float ELITE_MARKER_RADIUS = 2.5f;
        private const float ELITE_MARKER_SPEED = 10f; //Clients ease their markers to the host's positions
        private const float BOSS_DURATION = 120f;
        private const float BOSS_SIZE = 1.5f;
        private const float BOSS_MARKER_RADIUS = 4f;

        // The game's mini-bosses, tried in a random order until one spawns
        private static readonly EEnemy[] MiniBosses = [EEnemy.MinibossPig, EEnemy.MinibossGolem, EEnemy.MinibossGolemSand, EEnemy.MinibossScorpion];

        private static readonly Color ZONE_COLOR = new(0.75f, 0.35f, 1f, 1f);
        private static readonly Color ZONE_DONE_COLOR = new(0.4f, 1f, 0.55f, 1f);
        private static readonly Color SHARD_COLOR = new(1f, 0.82f, 0.3f, 1f);
        private static readonly Color STORM_COLOR = new(1f, 0.35f, 0.2f, 1f);
        private static readonly Color ELITE_COLOR = new(1f, 0.25f, 0.45f, 1f);
        private static readonly Color BOSS_COLOR = new(1f, 0.6f, 0.1f, 1f);

        private readonly IUdpClientService udpClientService;
        private readonly IPlayerManagerService playerManagerService;
        private readonly IEnemyManagerService enemyManagerService;
        private readonly List<Enemy> elites = []; //Host: the elite hunt's elites or the mini-boss, in the rift's order
        private readonly IAscensionService ascensionService;
        private readonly ICurseService curseService;
        // Resolved when used: the synchronization service depends on services that depend on this one
        private readonly Lazy<ISynchronizationService> synchronization = new(() => Plugin.Services.GetService<ISynchronizationService>());
        private readonly System.Random random = new();

        private Rift current;
        private int nextId = 1;
        private float stageTime;
        private float nextRiftAt = FIRST_RIFT_DELAY;
        private float nextProgressSendAt;
        private bool wasForceKeyDown;
        private Material lineMaterial;
        private TextMeshProUGUI hudText;
        private string resultText;
        private string lastHudText;
        private (int Id, int Seconds, long Progress) riftHudKey;
        private string riftHudText;
        private bool hudShown;
        private float stormRingRadius;
        private float resultShownUntil;
        private float stormDamageTimer;
        private bool isOutsideStorm;
        private HashSet<uint> deadAtStormStart = [];

        public int ClearedCount { get; private set; }

        private bool IsHost => udpClientService.IsHost() ?? false;

        private class Rift
        {
            public int Id;
            public RiftType Type;
            public Vector3[] Zones; //Zones to hold or shards to collect
            public float Radius;
            public float[] Progress;
            public float TimeLeft;
            public float Duration;
            public float StartRadius;
            public Vector3[] Targets; //Elite hunt on a client: the host's last positions, the markers ease to them

            /// <summary>
            /// A storm's circle now: shrinking from StartRadius to Radius, then staying
            /// </summary>
            public float StormRadius => StartRadius + (Radius - StartRadius) * Math.Clamp((Duration - TimeLeft) / STORM_SHRINK_SECONDS, 0f, 1f);
            public readonly List<(GameObject Marker, LineRenderer Ring, LineRenderer Beam)> Visuals = [];
        }

        public RiftService(IUdpClientService udpClientService, IPlayerManagerService playerManagerService, IEnemyManagerService enemyManagerService,
            IAscensionService ascensionService, ICurseService curseService)
        {
            this.udpClientService = udpClientService;
            this.playerManagerService = playerManagerService;
            this.enemyManagerService = enemyManagerService;
            this.ascensionService = ascensionService;
            this.curseService = curseService;

            EventManager.SubscribeRiftStartedEvents(started =>
            {
                if (!IsHost)
                {
                    Begin(started);
                }
            });
            EventManager.SubscribeRiftProgressEvents(progress =>
            {
                if (!IsHost && current != null && current.Id == progress.Id)
                {
                    current.Progress = progress.Progress;
                    current.TimeLeft = progress.TimeLeft;
                    if (progress.Positions.Length == current.Zones.Length)
                    {
                        current.Targets = [.. progress.Positions.Select(position => position.ToUnityVector3())];
                    }
                    RefreshVisuals();
                }
            });
            EventManager.SubscribeRiftEndedEvents(ended =>
            {
                if (!IsHost && current != null && current.Id == ended.Id)
                {
                    Finish(ended.Cleared);
                }
            });
            EventManager.SubscribeGameStartedEvents(() =>
            {
                stageTime = 0f;
                nextRiftAt = FIRST_RIFT_DELAY;
            });
            EventManager.SubscribePortalOpenedEvents(() => Cancel()); //Left the stage: no reward, no penalty
        }

        public void Tick()
        {
            UpdateStorm();
            UpdateEliteMarkers();
            UpdateHud();

            if (!IsHost || !synchronization.Value.HasNetplaySessionStarted() || synchronization.Value.IsEndgame())
            {
                return;
            }

            var forced = IsForceKeyPressed();
            var dt = MyTime.paused ? 0f : Time.deltaTime;

            if (current != null)
            {
                UpdateHostRift(dt);
                return;
            }

            if (MapController.isFinalBossStage)
            {
                return;
            }

            if (!forced)
            {
                if (ascensionService.Level <= 0)
                {
                    return;
                }

                stageTime += dt;
                if (stageTime < nextRiftAt)
                {
                    return;
                }
            }

            StartHostRift();
        }

        /// <summary>
        /// Debug tools: F9 on the host opens a rift now (even before any ascension)
        /// </summary>
        private bool IsForceKeyPressed()
        {
            var isDown = DebugTools.Enabled && BepInEx.Unity.IL2CPP.UnityEngine.Input.GetKeyInt((BepInEx.Unity.IL2CPP.UnityEngine.KeyCode)KeyCode.F9);
            var isPressed = isDown && !wasForceKeyDown;
            wasForceKeyDown = isDown;
            return isPressed;
        }

        private void StartHostRift()
        {
            var anchors = GetActivePlayerPositions();
            if (anchors.Count == 0)
            {
                nextRiftAt = stageTime + 10f; //Nobody up to hold a zone: soon
                return;
            }

            var type = (RiftType)random.Next(5);
            if ((type == RiftType.EliteHunt && StartHostEliteHunt(anchors)) || (type == RiftType.MiniBoss && StartHostMiniBoss(anchors)))
            {
                return;
            }

            if (type is RiftType.Storm or RiftType.EliteHunt or RiftType.MiniBoss) //Nothing could spawn: a storm instead
            {
                StartHostStorm(anchors);
                return;
            }

            var playerCount = playerManagerService.GetAllPlayers().Count();
            var (count, clearRadius, spacing, radius) = type == RiftType.Shards
                ? (Math.Clamp(playerCount * SHARDS_PER_PLAYER, MIN_SHARDS, MAX_SHARDS), SHARD_CLEAR_RADIUS, SHARD_SPACING, SHARD_PICKUP_RADIUS)
                : (Math.Clamp(playerCount, 1, MAX_ZONES), ZONE_RADIUS, ZONE_RADIUS * 3f, ZONE_RADIUS);

            var zones = new List<Vector3>();
            var groundLayer = GetGroundLayer();
            for (var i = 0; i < count; i++)
            {
                zones.Add(PickZonePosition(anchors[i % anchors.Count], zones, groundLayer, clearRadius, spacing));
            }

            var started = new RiftStarted
            {
                Id = nextId++,
                Type = type,
                Zones = [.. zones.Select(zone => zone.ToNumericsVector3())],
                ZoneRadius = radius,
                Duration = DURATION
            };
            udpClientService.SendToAllClients(started, LiteNetLib.DeliveryMethod.ReliableOrdered);
            Begin(started);
            nextProgressSendAt = 0f;
        }

        /// <summary>
        /// Around the party: the circle starts wide enough to hold everyone with room to spare
        /// </summary>
        private void StartHostStorm(List<Vector3> anchors)
        {
            var center = PickZonePositionNear(anchors[random.Next(anchors.Count)], [], 0f);
            var startRadius = Math.Max(STORM_MIN_START_RADIUS, anchors.Max(anchor => HorizontalDistance(anchor, center)) + STORM_START_MARGIN);
            deadAtStormStart = [.. playerManagerService.GetAllPlayers().Where(player => playerManagerService.IsDead(player.ConnectionId)).Select(player => player.ConnectionId)];

            var started = new RiftStarted
            {
                Id = nextId++,
                Type = RiftType.Storm,
                Zones = [center.ToNumericsVector3()],
                ZoneRadius = STORM_END_RADIUS,
                StartRadius = startRadius,
                Duration = STORM_DURATION
            };
            udpClientService.SendToAllClients(started, LiteNetLib.DeliveryMethod.ReliableOrdered);
            Begin(started);
            nextProgressSendAt = 0f;
        }

        /// <summary>
        /// Elites of the stage's own kinds, around the players. Spawned by the game's enemy manager, so the clients get
        /// them like any spawn. False when no ordinary enemy is around to copy.
        /// </summary>
        private bool StartHostEliteHunt(List<Vector3> anchors)
        {
            var kinds = enemyManagerService.GetAllSpawnedEnemies()
                .Where(enemy => enemy != null && enemy.CachedPtr() != IntPtr.Zero && !enemy.IsDead() && !enemy.IsElite()
                    && !enemy.IsBoss() && !enemy.IsStageBoss() && !enemy.IsFinalBoss() && enemy.enemyData != null)
                .Select(enemy => enemy.enemyData)
                .ToList();
            var manager = EnemyManager.Instance;
            if (kinds.Count == 0 || manager == null)
            {
                return false;
            }

            var playerCount = playerManagerService.GetAllPlayers().Count();
            var count = Math.Clamp(playerCount + 1, MIN_ELITES, MAX_ELITES);
            var positions = new List<Vector3>();
            elites.Clear();
            for (var i = 0; i < count; i++)
            {
                var position = PickZonePositionNear(anchors[i % anchors.Count], positions, 8f) + Vector3.up * 0.5f;
                try
                {
                    var elite = manager.SpawnEnemy(kinds[random.Next(kinds.Count)], position, 0, true, EEnemyFlag.Elite, true, ELITE_SIZE);
                    if (elite != null)
                    {
                        elites.Add(elite);
                        positions.Add(position);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Rift elite spawn failed: {ex.Message}");
                }
            }

            if (elites.Count == 0)
            {
                return false;
            }

            var started = new RiftStarted
            {
                Id = nextId++,
                Type = RiftType.EliteHunt,
                Zones = [.. positions.Select(position => position.ToNumericsVector3())],
                ZoneRadius = ELITE_MARKER_RADIUS,
                Duration = ELITE_DURATION
            };
            udpClientService.SendToAllClients(started, LiteNetLib.DeliveryMethod.ReliableOrdered);
            Begin(started);
            nextProgressSendAt = 0f;
            return true;
        }

        /// <summary>
        /// One of the game's mini-bosses near a player, with the game's mini-boss flag. Spawned by the game's enemy
        /// manager like its own bosses, so the clients get it like any spawn.
        /// </summary>
        private bool StartHostMiniBoss(List<Vector3> anchors)
        {
            var manager = EnemyManager.Instance;
            if (manager == null)
            {
                return false;
            }

            var position = PickZonePositionNear(anchors[random.Next(anchors.Count)], [], 0f) + Vector3.up * 0.5f;
            elites.Clear();
            foreach (var kind in MiniBosses.OrderBy(_ => random.Next()))
            {
                try
                {
                    var boss = manager.SpawnBoss(kind, 0, EEnemyFlag.SummonerMiniboss, position, BOSS_SIZE);
                    if (boss != null)
                    {
                        elites.Add(boss);
                        Plugin.Log.LogInfo($"Rift mini-boss: {kind}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Rift mini-boss {kind} could not spawn: {ex.Message}");
                }
            }

            if (elites.Count == 0)
            {
                return false;
            }

            var started = new RiftStarted
            {
                Id = nextId++,
                Type = RiftType.MiniBoss,
                Zones = [position.ToNumericsVector3()],
                ZoneRadius = BOSS_MARKER_RADIUS,
                Duration = BOSS_DURATION
            };
            udpClientService.SendToAllClients(started, LiteNetLib.DeliveryMethod.ReliableOrdered);
            Begin(started);
            nextProgressSendAt = 0f;
            return true;
        }

        /// <summary>
        /// An elite or the mini-boss is done once dead, or gone from the stage some other way (nothing left to hunt). The
        /// mini-boss's progress meanwhile is the HP it lost.
        /// </summary>
        private void UpdateHostElites(float dt)
        {
            for (var i = 0; i < elites.Count && i < current.Progress.Length; i++)
            {
                if (current.Progress[i] >= 1f)
                {
                    continue;
                }

                var elite = elites[i];
                try
                {
                    if (elite == null || elite.CachedPtr() == IntPtr.Zero || elite.IsDeadOrDyingNextFrame() || !elite.gameObject.activeInHierarchy)
                    {
                        current.Progress[i] = 1f; //Never back: a pooled enemy may come back as another one
                        continue;
                    }

                    current.Zones[i] = elite.transform.position;
                    if (current.Type == RiftType.MiniBoss)
                    {
                        current.Progress[i] = Math.Clamp(1f - elite.GetHpRatio(), 0f, 0.99f);
                    }
                }
                catch
                {
                    current.Progress[i] = 1f;
                }
            }

            current.TimeLeft = Math.Max(0f, current.TimeLeft - dt);
            var cleared = current.Progress.All(progress => progress >= 1f);
            SendProgressAndEnd(cleared, !cleared && current.TimeLeft <= 0f);
        }

        /// <summary>
        /// Every machine: the markers follow the living elites (the host's own positions, eased on the clients)
        /// </summary>
        private void UpdateEliteMarkers()
        {
            if (current == null || current.Type is not (RiftType.EliteHunt or RiftType.MiniBoss))
            {
                return;
            }

            for (var i = 0; i < current.Visuals.Count && i < current.Zones.Length; i++)
            {
                if (!IsHost && current.Targets != null && i < current.Targets.Length)
                {
                    current.Zones[i] = Vector3.Lerp(current.Zones[i], current.Targets[i], Math.Min(1f, Time.deltaTime * ELITE_MARKER_SPEED));
                }

                var marker = current.Visuals[i].Marker;
                if (marker == null)
                {
                    continue;
                }

                // The ring and the beam are drawn in the marker's space: they follow it
                marker.transform.position = current.Zones[i];
            }
        }

        /// <summary>
        /// Every machine, every frame of a storm: its circle shrinks, and the local player outside of it gets hurt
        /// </summary>
        private void UpdateStorm()
        {
            isOutsideStorm = false;
            if (current == null || current.Type != RiftType.Storm)
            {
                return;
            }

            var center = current.Zones[0];
            var radius = current.StormRadius;
            // Redrawn by visible steps while it shrinks (48 native writes, not every frame), then once at its final size
            var shrinking = current.Duration - current.TimeLeft < STORM_SHRINK_SECONDS;
            if (radius != stormRingRadius && (!shrinking || Math.Abs(radius - stormRingRadius) >= STORM_RING_STEP)
                && current.Visuals.Count > 0 && current.Visuals[0].Ring != null)
            {
                SetRingRadius(current.Visuals[0].Ring, radius);
                stormRingRadius = radius;
            }

            var player = GameManager.Instance?.player;
            var health = player?.inventory?.playerHealth;
            var localPlayer = playerManagerService.GetLocalPlayer();
            if (player == null || health == null || localPlayer == null || health.IsDead() || playerManagerService.IsDowned(localPlayer.ConnectionId))
            {
                return;
            }

            isOutsideStorm = HorizontalDistance(player.transform.position, center) > radius;
            if (!isOutsideStorm || MyTime.paused)
            {
                stormDamageTimer = 0f;
                return;
            }

            stormDamageTimer += Time.deltaTime;
            if (stormDamageTimer < STORM_DAMAGE_INTERVAL)
            {
                return;
            }

            stormDamageTimer = 0f;
            try
            {
                // The game's own damage path: armor, shield, downed state and death as usual
                health.DamagePlayerExternal(health.GetCombinedMaxHp() * STORM_DAMAGE_SHARE, 0f, Vector3.zero, false, "Megaphonk storm", default(DcFlags), default(EDamageEffect), null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Storm damage failed: {ex}");
            }
        }

        /// <summary>
        /// The ring's points around its marker (rings are drawn in their marker's space)
        /// </summary>
        private static void SetRingRadius(LineRenderer ring, float radius)
        {
            for (var i = 0; i < RING_SEGMENTS; i++)
            {
                var angle = i * Math.PI * 2 / RING_SEGMENTS;
                ring.SetPosition(i, new Vector3((float)Math.Cos(angle) * radius, 0.4f, (float)Math.Sin(angle) * radius));
            }
        }

        private List<Vector3> GetActivePlayerPositions()
        {
            var positions = new List<Vector3>();
            foreach (var player in playerManagerService.GetAllPlayersAlive())
            {
                if (playerManagerService.IsDowned(player.ConnectionId))
                {
                    continue;
                }

                var transform = playerManagerService.GetTargetTransform(player.ConnectionId);
                if (transform != null)
                {
                    positions.Add(transform.position);
                }
            }
            return positions;
        }

        /// <summary>
        /// A random spot anywhere on the map where a circle of the given radius lies on even, open ground (no wall in it),
        /// at least spacing away from the others. Near a player when the map gives none.
        /// </summary>
        private Vector3 PickZonePosition(Vector3 anchor, List<Vector3> others, int groundLayer, float radius, float spacing)
        {
            var size = Plugin.Instance.OriginalWorldSize;
            var halfX = size.x * 0.45f;
            var halfZ = size.z * 0.45f;
            if (halfX > radius * 2f && halfZ > radius * 2f)
            {
                for (var attempt = 0; attempt < MAP_ATTEMPTS; attempt++)
                {
                    var x = (float)(random.NextDouble() * 2 - 1) * halfX;
                    var z = (float)(random.NextDouble() * 2 - 1) * halfZ;
                    if (!TryGetGround(new Vector3(x, anchor.y + SKY_HEIGHT, z), SKY_HEIGHT * 3f, out var center, out var layer)
                        || (groundLayer >= 0 && layer != groundLayer)
                        || others.Any(other => HorizontalDistance(other, center) < spacing))
                    {
                        continue;
                    }

                    if (IsCircleClear(center, layer, radius))
                    {
                        return center;
                    }
                }
            }

            Plugin.Log.LogInfo("Rift: no open spot found on the map, the zone goes near a player");
            return PickZonePositionNear(anchor, others, spacing);
        }

        /// <summary>
        /// The first ground below a point (null when only sky or a trigger)
        /// </summary>
        private static bool TryGetGround(Vector3 from, float length, out Vector3 point, out int layer)
        {
            point = from;
            layer = -1;
            if (!Physics.Raycast(new Ray(from, Vector3.up * -1f), out var hit, length, ~0, QueryTriggerInteraction.Ignore) || hit.collider == null)
            {
                return false;
            }

            point = hit.point;
            layer = hit.collider.gameObject.layer;
            return hit.normal.y > 0.8f; //Not a slope or a wall
        }

        /// <summary>
        /// Even ground all around the circle, same surface, and no wall between its center and its edge
        /// </summary>
        private bool IsCircleClear(Vector3 center, int layer, float radius)
        {
            for (var i = 0; i < CIRCLE_CHECKS; i++)
            {
                var angle = i * Math.PI * 2 / CIRCLE_CHECKS;
                var direction = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));

                if (!TryGetGround(center + direction * radius + Vector3.up * 10f, 20f, out var edge, out var edgeLayer)
                    || edgeLayer != layer || Math.Abs(edge.y - center.y) > MAX_GROUND_STEP)
                {
                    return false;
                }

                if (Physics.Raycast(new Ray(center + Vector3.up * 1.5f, direction), out _, radius, ~0, QueryTriggerInteraction.Ignore))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The layer of the ground under the host: zones go on the same kind of surface (not on a roof or a rock)
        /// </summary>
        private static int GetGroundLayer()
        {
            var player = GameManager.Instance?.player?.transform;
            if (player == null)
            {
                return -1;
            }

            // The player's own colliders are skipped: the ground is the first other thing below
            var origin = player.position + Vector3.up * 1f;
            var remaining = 10f;
            for (var i = 0; i < 6 && remaining > 0f; i++)
            {
                if (!Physics.Raycast(new Ray(origin, Vector3.up * -1f), out var hit, remaining, ~0, QueryTriggerInteraction.Ignore) || hit.collider == null)
                {
                    return -1;
                }

                if (!hit.collider.transform.IsChildOf(player))
                {
                    return hit.collider.gameObject.layer;
                }

                var skipped = hit.distance + 0.05f;
                origin += Vector3.up * -skipped;
                remaining -= skipped;
            }

            return -1;
        }

        /// <summary>
        /// A spot on the ground some way from a player, away from the other zones
        /// </summary>
        private Vector3 PickZonePositionNear(Vector3 anchor, List<Vector3> others, float spacing)
        {
            var fallback = anchor;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var angle = (float)(random.NextDouble() * Math.PI * 2);
                var distance = MIN_ZONE_DISTANCE + (float)random.NextDouble() * (MAX_ZONE_DISTANCE - MIN_ZONE_DISTANCE);
                var candidate = anchor + new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)) * distance;
                if (attempt == 0)
                {
                    fallback = candidate;
                }

                if (!Physics.Raycast(new Ray(candidate + Vector3.up * 60f, Vector3.up * -1f), out var hit, 150f, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                var point = hit.point;
                if (Math.Abs(point.y - anchor.y) > 25f || others.Any(other => HorizontalDistance(other, point) < spacing))
                {
                    continue; //A cliff or a hole, or too close to another zone
                }

                return point;
            }

            return fallback;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private void UpdateHostRift(float dt)
        {
            if (current.Type == RiftType.Storm)
            {
                UpdateHostStorm(dt);
                return;
            }

            if (current.Type is RiftType.EliteHunt or RiftType.MiniBoss)
            {
                UpdateHostElites(dt);
                return;
            }

            if (dt > 0f)
            {
                var players = GetActivePlayerPositions();
                for (var i = 0; i < current.Zones.Length; i++)
                {
                    var zone = current.Zones[i];
                    var holders = players.Count(position =>
                        Math.Abs(position.y - zone.y) < ZONE_HEIGHT_TOLERANCE && HorizontalDistance(position, zone) <= current.Radius);
                    if (holders == 0)
                    {
                        continue;
                    }

                    // A shard is picked up at once; each player in a zone adds to the speed (two hold it twice as fast)
                    current.Progress[i] = current.Type == RiftType.Shards ? 1f : Math.Min(1f, current.Progress[i] + dt * holders / HOLD_SECONDS);
                }
                current.TimeLeft = Math.Max(0f, current.TimeLeft - dt);
            }

            var cleared = current.Progress.All(progress => progress >= 1f);
            var failed = !cleared && current.TimeLeft <= 0f;
            SendProgressAndEnd(cleared, failed);
        }

        /// <summary>
        /// Survived when the time is up with nobody down; failed as soon as someone dies (a downed player can still be
        /// revived)
        /// </summary>
        private void UpdateHostStorm(float dt)
        {
            current.TimeLeft = Math.Max(0f, current.TimeLeft - dt);
            current.Progress[0] = 1f - current.TimeLeft / current.Duration;

            var players = playerManagerService.GetAllPlayers().ToList();
            var someoneDied = players.Any(player => playerManagerService.IsDead(player.ConnectionId) && !deadAtStormStart.Contains(player.ConnectionId));
            var timeUp = current.TimeLeft <= 0f;
            var someoneDown = players.Any(player => playerManagerService.IsDowned(player.ConnectionId));

            SendProgressAndEnd(timeUp && !someoneDied && !someoneDown, someoneDied || (timeUp && someoneDown));
        }

        private void SendProgressAndEnd(bool cleared, bool failed)
        {
            if (cleared || failed || Time.realtimeSinceStartup >= nextProgressSendAt)
            {
                nextProgressSendAt = Time.realtimeSinceStartup + PROGRESS_SEND_INTERVAL;
                udpClientService.SendToAllClients(new RiftProgress
                {
                    Id = current.Id,
                    Progress = [.. current.Progress],
                    TimeLeft = current.TimeLeft,
                    Positions = (current.Type is RiftType.EliteHunt or RiftType.MiniBoss) ? [.. current.Zones.Select(zone => zone.ToNumericsVector3())] : []
                }, LiteNetLib.DeliveryMethod.ReliableOrdered);
                RefreshVisuals();
            }

            if (cleared || failed)
            {
                udpClientService.SendToAllClients(new RiftEnded { Id = current.Id, Cleared = cleared }, LiteNetLib.DeliveryMethod.ReliableOrdered);
                Finish(cleared);
                nextRiftAt = stageTime + RIFT_INTERVAL;
            }
        }

        private void Begin(RiftStarted started)
        {
            Cancel();

            current = new Rift
            {
                Id = started.Id,
                Type = started.Type,
                Zones = [.. started.Zones.Select(zone => zone.ToUnityVector3())],
                Radius = started.ZoneRadius,
                Progress = new float[started.Zones.Length],
                TimeLeft = started.Duration,
                Duration = started.Duration,
                StartRadius = started.StartRadius
            };
            stormDamageTimer = 0f;
            stormRingRadius = current.StartRadius;
            riftHudText = null; //Never the previous rift's text: ids start over with another host

            try
            {
                foreach (var zone in current.Zones)
                {
                    current.Visuals.Add(current.Type switch
                    {
                        RiftType.Shards => CreateZoneVisual(zone, current.Radius, SHARD_COLOR),
                        RiftType.Storm => CreateZoneVisual(zone, current.StartRadius, STORM_COLOR),
                        RiftType.EliteHunt => CreateZoneVisual(zone, current.Radius, ELITE_COLOR),
                        RiftType.MiniBoss => CreateZoneVisual(zone, current.Radius, BOSS_COLOR),
                        _ => CreateZoneVisual(zone, current.Radius, ZONE_COLOR)
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Rift zone visuals failed: {ex}");
            }

            Plugin.Log.LogInfo($"Rift {current.Id} opened: {current.Type}, {current.Zones.Length} within {started.Duration}s at {string.Join(", ", current.Zones)}");
            var description = current.Type switch
            {
                RiftType.Shards => "Rift_Shards_Description",
                RiftType.Storm => "Rift_Storm_Description",
                RiftType.EliteHunt => "Rift_Elites_Description",
                RiftType.MiniBoss => "Rift_MiniBoss_Description",
                _ => "Rift_Description"
            };
            Plugin.StartNotification(("Megaphonk", "Rift"), ("Megaphonk", description), []);
        }

        private void Finish(bool cleared)
        {
            if (current == null)
            {
                return;
            }

            Plugin.Log.LogInfo($"Rift {current.Id} {(cleared ? "cleared" : "failed")}");
            if (cleared)
            {
                ClearedCount++;
            }

            RemoveVisuals();
            current = null;
            elites.Clear();
            resultText = cleared ? Loc.T("Rift cleared!") : Loc.T("Rift failed...");
            resultShownUntil = Time.realtimeSinceStartup + RESULT_SHOWN_SECONDS;
            curseService.QueueRiftChoice(cleared);
        }

        /// <summary>
        /// Stage left or session over while a rift is open: it just closes
        /// </summary>
        private void Cancel()
        {
            if (current == null)
            {
                return;
            }

            Plugin.Log.LogInfo($"Rift {current.Id} closed with the stage");
            RemoveVisuals();
            current = null;
            elites.Clear();
        }

        private (GameObject, LineRenderer, LineRenderer) CreateZoneVisual(Vector3 center, float radius, Color color)
        {
            var marker = new GameObject("Megaphonk_RiftZone");
            marker.transform.position = center;

            var ring = new GameObject("Ring").AddComponent<LineRenderer>();
            ring.transform.SetParent(marker.transform, false);
            ring.useWorldSpace = false; //In the marker's space: moving the marker moves the ring and the beam
            ring.loop = true;
            ring.positionCount = RING_SEGMENTS;
            SetRingRadius(ring, radius);
            ring.startWidth = 0.6f;
            ring.endWidth = 0.6f;

            var beam = new GameObject("Beam").AddComponent<LineRenderer>();
            beam.transform.SetParent(marker.transform, false);
            beam.useWorldSpace = false;
            beam.positionCount = 2;
            beam.SetPosition(0, Vector3.zero);
            beam.SetPosition(1, new Vector3(0f, BEAM_HEIGHT, 0f));
            beam.startWidth = 3f;
            beam.endWidth = 1.5f;

            var material = GetLineMaterial();
            if (material != null)
            {
                ring.material = material;
                beam.material = material;
            }
            SetColor(ring, beam, color);

            GetMinimap()?.AddArrow(marker.transform, color);
            return (marker, ring, beam);
        }

        private static void SetColor(LineRenderer ring, LineRenderer beam, Color color)
        {
            ring.startColor = color;
            ring.endColor = color;
            beam.startColor = color;
            beam.endColor = new Color(color.r, color.g, color.b, 0.4f);
        }

        /// <summary>
        /// A zone turns green once held long enough, a picked up shard goes away
        /// </summary>
        private void RefreshVisuals()
        {
            if (current == null)
            {
                return;
            }

            for (var i = 0; i < current.Visuals.Count && i < current.Progress.Length; i++)
            {
                var (marker, ring, beam) = current.Visuals[i];
                if (marker == null)
                {
                    continue;
                }

                if (current.Type is RiftType.Shards or RiftType.EliteHunt or RiftType.MiniBoss)
                {
                    if (current.Progress[i] >= 1f)
                    {
                        GetMinimap()?.RemoveArrow(marker.transform);
                        UnityEngine.Object.Destroy(marker);
                    }
                    continue;
                }

                SetColor(ring, beam, current.Progress[i] >= 1f ? ZONE_DONE_COLOR : ZONE_COLOR);
            }
        }

        private void RemoveVisuals()
        {
            foreach (var (marker, _, _) in current?.Visuals ?? [])
            {
                if (marker != null)
                {
                    GetMinimap()?.RemoveArrow(marker.transform);
                    UnityEngine.Object.Destroy(marker);
                }
            }
            current?.Visuals.Clear();
        }

        /// <summary>
        /// Seen through walls at any distance, else the ping's plain material
        /// </summary>
        private Material GetLineMaterial()
        {
            var onTop = XRayMaterials.OnTop();
            if (onTop != null)
            {
                return onTop;
            }

            if (lineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                if (shader == null)
                {
                    Plugin.Log.LogWarning("No shader found for rift zones");
                    return null;
                }
                lineMaterial = new Material(shader);
            }

            return lineMaterial;
        }

        private static MinimapCamera GetMinimap()
        {
            return GameManager.Instance?.player?.minimapCamera?.GetComponent<MinimapCamera>();
        }

        /// <summary>
        /// Under the team meter and the stage timer: the zones' progress and the time left, then the result for a few seconds
        /// </summary>
        private string BuildRiftHudText()
        {
            var objective = current.Type switch
            {
                RiftType.Shards => $"{Loc.T("collect the shards")}  {current.Progress.Count(progress => progress >= 1f)}/{current.Progress.Length}",
                RiftType.MiniBoss => $"{Loc.T("defeat the mini-boss")}  {Loc.T("HP")} {(int)Math.Ceiling((1f - current.Progress[0]) * 100f)}%",
                RiftType.EliteHunt => $"{Loc.T("hunt down the elites")}  {current.Progress.Count(progress => progress >= 1f)}/{current.Progress.Length}",
                RiftType.Storm => $"{Loc.T("survive the storm, stay in the circle")}{(isOutsideStorm ? $"  <color=#FF5555>{Loc.T("OUTSIDE THE CIRCLE!")}</color>" : string.Empty)}",
                _ => $"{Loc.T("hold the zones")}  {string.Join("  ", current.Progress.Select(progress => progress >= 1f ? "<color=#66FF8C>100%</color>" : $"{(int)(progress * 100f)}%"))}"
            };
            var time = TimeSpan.FromSeconds(Math.Ceiling(current.TimeLeft));
            return $"<color=#C08CFF>{Loc.T("Rift")}</color>: {objective}  <color=#FFD27F>{time.Minutes}:{time.Seconds:00}</color>";
        }

        /// <summary>
        /// The progress as the HUD shows it, as one number: the text is built again when it changes
        /// </summary>
        private long HudProgressKey()
        {
            switch (current.Type)
            {
                case RiftType.Shards:
                case RiftType.EliteHunt:
                    var done = 0;
                    foreach (var progress in current.Progress)
                    {
                        if (progress >= 1f)
                        {
                            done++;
                        }
                    }
                    return done;
                case RiftType.MiniBoss:
                    return (int)Math.Ceiling((1f - current.Progress[0]) * 100f);
                case RiftType.Storm:
                    return isOutsideStorm ? 1 : 0;
                default:
                    long percents = 0;
                    foreach (var progress in current.Progress)
                    {
                        percents = percents * 101 + (progress >= 1f ? 100 : (int)(progress * 100f));
                    }
                    return percents;
            }
        }

        private void UpdateHud()
        {
            string text = null;
            if (current != null)
            {
                // Built again only when what it shows changed (each second at least): a new string every frame was garbage
                var key = (current.Id, (int)Math.Ceiling(current.TimeLeft), HudProgressKey());
                if (riftHudText == null || key != riftHudKey)
                {
                    riftHudKey = key;
                    riftHudText = BuildRiftHudText();
                }
                text = riftHudText;
            }
            else if (resultText != null && Time.realtimeSinceStartup < resultShownUntil)
            {
                text = resultText;
            }

            if (text == null)
            {
                if (hudShown && hudText != null)
                {
                    hudText.enabled = false;
                }
                hudShown = false;
                return;
            }

            if (hudText == null)
            {
                if (UiManager.Instance == null)
                {
                    return;
                }

                lastHudText = null;
                var go = new GameObject("Megaphonk_RiftHud");
                hudText = go.AddComponent<TextMeshProUGUI>();
                hudText.transform.SetParent(UiManager.Instance.transform, false);
                hudText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                hudText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                hudText.rectTransform.pivot = new Vector2(0.5f, 1f);
                hudText.rectTransform.anchoredPosition = new Vector2(0f, -100f); //Under the stage timer
                hudText.rectTransform.sizeDelta = new Vector2(900f, 40f);
                hudText.alignment = TextAlignmentOptions.Center;
                hudText.fontSize = 26;
                hudText.fontStyle = FontStyles.Bold;
                hudText.outlineColor = Color.black;
                hudText.outlineWidth = 0.2f;
                hudText.richText = true;
                hudText.color = Color.white;
            }

            if (!hudShown)
            {
                hudText.enabled = true;
                hudShown = true;
            }

            if (text != lastHudText)
            {
                hudText.text = text;
                lastHudText = text;
            }
        }

        public void Reset()
        {
            Cancel();
            ClearedCount = 0;
            stageTime = 0f;
            nextRiftAt = FIRST_RIFT_DELAY;
            resultText = null;
            if (hudText != null)
            {
                hudText.enabled = false;
            }
            hudShown = false;
        }
    }
}
