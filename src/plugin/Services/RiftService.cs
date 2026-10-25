using Assets.Scripts.Camera;
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

namespace Megaphonk.Services
{
    /// <summary>
    /// Rifts, the co-op endgame's objectives: from the first ascension, every stage but the final boss's opens one now
    /// and then. For now one kind, zones to hold: each fills while a player (alive, not downed) stands in it, all of them
    /// before the time runs out. Cleared, every player picks a pact; failed, a curse (CurseService). A rift adds no
    /// enemy: the stage's own ones are the threat. The host runs it and sends its progress; every machine shows it.
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
        private const float DURATION = 90f;
        private const float HOLD_SECONDS = 20f;
        private const float ZONE_RADIUS = 9f;
        private const float ZONE_HEIGHT_TOLERANCE = 8f;
        private const float MIN_ZONE_DISTANCE = 20f;
        private const float MAX_ZONE_DISTANCE = 35f;
        private const int MAX_ZONES = 2;
        private const float PROGRESS_SEND_INTERVAL = 0.25f;
        private const float RESULT_SHOWN_SECONDS = 4f;
        private const int RING_SEGMENTS = 48;
        private const float BEAM_HEIGHT = 30f;

        private static readonly Color ZONE_COLOR = new(0.75f, 0.35f, 1f, 1f);
        private static readonly Color ZONE_DONE_COLOR = new(0.4f, 1f, 0.55f, 1f);

        private readonly IUdpClientService udpClientService;
        private readonly IPlayerManagerService playerManagerService;
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
        private float resultShownUntil;

        public int ClearedCount { get; private set; }

        private bool IsHost => udpClientService.IsHost() ?? false;

        private class Rift
        {
            public int Id;
            public Vector3[] Zones;
            public float Radius;
            public float[] Progress;
            public float TimeLeft;
            public readonly List<(GameObject Marker, LineRenderer Ring, LineRenderer Beam)> Visuals = [];
        }

        public RiftService(IUdpClientService udpClientService, IPlayerManagerService playerManagerService, IAscensionService ascensionService, ICurseService curseService)
        {
            this.udpClientService = udpClientService;
            this.playerManagerService = playerManagerService;
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
            UpdateHud();

            if (!IsHost || udpClientService.IsSoloSession() || !synchronization.Value.HasNetplaySessionStarted() || synchronization.Value.IsEndgame())
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

            var zones = new List<Vector3>();
            var zoneCount = Math.Clamp(playerManagerService.GetAllPlayers().Count(), 1, MAX_ZONES);
            for (var i = 0; i < zoneCount; i++)
            {
                zones.Add(PickZonePosition(anchors[i % anchors.Count], zones));
            }

            var started = new RiftStarted
            {
                Id = nextId++,
                Type = RiftType.HoldZones,
                Zones = [.. zones.Select(zone => zone.ToNumericsVector3())],
                ZoneRadius = ZONE_RADIUS,
                Duration = DURATION
            };
            udpClientService.SendToAllClients(started, LiteNetLib.DeliveryMethod.ReliableOrdered);
            Begin(started);
            nextProgressSendAt = 0f;
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
        /// A spot on the ground some way from a player, away from the other zones
        /// </summary>
        private Vector3 PickZonePosition(Vector3 anchor, List<Vector3> others)
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
                if (Math.Abs(point.y - anchor.y) > 25f || others.Any(other => HorizontalDistance(other, point) < ZONE_RADIUS * 3f))
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
            if (dt > 0f)
            {
                var players = GetActivePlayerPositions();
                for (var i = 0; i < current.Zones.Length; i++)
                {
                    var zone = current.Zones[i];
                    var isHeld = players.Any(position =>
                        Math.Abs(position.y - zone.y) < ZONE_HEIGHT_TOLERANCE && HorizontalDistance(position, zone) <= current.Radius);
                    if (isHeld)
                    {
                        current.Progress[i] = Math.Min(1f, current.Progress[i] + dt / HOLD_SECONDS);
                    }
                }
                current.TimeLeft = Math.Max(0f, current.TimeLeft - dt);
            }

            var cleared = current.Progress.All(progress => progress >= 1f);
            var failed = !cleared && current.TimeLeft <= 0f;

            if (cleared || failed || Time.realtimeSinceStartup >= nextProgressSendAt)
            {
                nextProgressSendAt = Time.realtimeSinceStartup + PROGRESS_SEND_INTERVAL;
                udpClientService.SendToAllClients(new RiftProgress
                {
                    Id = current.Id,
                    Progress = [.. current.Progress],
                    TimeLeft = current.TimeLeft
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
                Zones = [.. started.Zones.Select(zone => zone.ToUnityVector3())],
                Radius = started.ZoneRadius,
                Progress = new float[started.Zones.Length],
                TimeLeft = started.Duration
            };

            try
            {
                foreach (var zone in current.Zones)
                {
                    current.Visuals.Add(CreateZoneVisual(zone, current.Radius));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Rift zone visuals failed: {ex}");
            }

            Plugin.Log.LogInfo($"Rift {current.Id} opened: hold {current.Zones.Length} zone(s) for {HOLD_SECONDS}s within {started.Duration}s at {string.Join(", ", current.Zones)}");
            Plugin.StartNotification(("Megaphonk", "Rift"), ("Megaphonk", "Rift_Description"), []);
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
        }

        private (GameObject, LineRenderer, LineRenderer) CreateZoneVisual(Vector3 center, float radius)
        {
            var marker = new GameObject("Megaphonk_RiftZone");
            marker.transform.position = center;

            var ring = new GameObject("Ring").AddComponent<LineRenderer>();
            ring.transform.SetParent(marker.transform, false);
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = RING_SEGMENTS;
            for (var i = 0; i < RING_SEGMENTS; i++)
            {
                var angle = i * Math.PI * 2 / RING_SEGMENTS;
                ring.SetPosition(i, center + new Vector3((float)Math.Cos(angle) * radius, 0.4f, (float)Math.Sin(angle) * radius));
            }
            ring.startWidth = 0.4f;
            ring.endWidth = 0.4f;

            var beam = new GameObject("Beam").AddComponent<LineRenderer>();
            beam.transform.SetParent(marker.transform, false);
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.SetPosition(0, center);
            beam.SetPosition(1, center + Vector3.up * BEAM_HEIGHT);
            beam.startWidth = 1.2f;
            beam.endWidth = 0.2f;

            var material = GetLineMaterial();
            if (material != null)
            {
                ring.material = material;
                beam.material = material;
            }
            SetColor(ring, beam, ZONE_COLOR);

            GetMinimap()?.AddArrow(marker.transform, ZONE_COLOR);
            return (marker, ring, beam);
        }

        private static void SetColor(LineRenderer ring, LineRenderer beam, Color color)
        {
            ring.startColor = color;
            ring.endColor = color;
            beam.startColor = color;
            beam.endColor = new Color(color.r, color.g, color.b, 0.1f);
        }

        /// <summary>
        /// A zone turns green once held long enough
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
                if (marker != null)
                {
                    SetColor(ring, beam, current.Progress[i] >= 1f ? ZONE_DONE_COLOR : ZONE_COLOR);
                }
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

        private Material GetLineMaterial()
        {
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
        /// Under the team meter: the zones' progress and the time left, then the result for a few seconds
        /// </summary>
        private void UpdateHud()
        {
            string text = null;
            if (current != null)
            {
                var zones = string.Join("  ", current.Progress.Select(progress =>
                    progress >= 1f ? "<color=#66FF8C>100%</color>" : $"{(int)(progress * 100f)}%"));
                var time = TimeSpan.FromSeconds(Math.Ceiling(current.TimeLeft));
                text = $"<color=#C08CFF>{Loc.T("Rift")}</color>: {Loc.T("hold the zones")}  {zones}  <color=#FFD27F>{time.Minutes}:{time.Seconds:00}</color>";
            }
            else if (resultText != null && Time.realtimeSinceStartup < resultShownUntil)
            {
                text = resultText;
            }

            if (text == null)
            {
                if (hudText != null)
                {
                    hudText.enabled = false;
                }
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
                hudText.rectTransform.anchoredPosition = new Vector2(0f, -48f);
                hudText.rectTransform.sizeDelta = new Vector2(900f, 40f);
                hudText.alignment = TextAlignmentOptions.Center;
                hudText.fontSize = 26;
                hudText.fontStyle = FontStyles.Bold;
                hudText.outlineColor = Color.black;
                hudText.outlineWidth = 0.2f;
                hudText.richText = true;
                hudText.color = Color.white;
            }

            hudText.enabled = true;
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
        }
    }
}
