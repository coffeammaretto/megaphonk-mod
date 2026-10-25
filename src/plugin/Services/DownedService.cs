using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using Megaphonk.Common.Messages;
using Megaphonk.Common.Messages.GameNetworkMessages;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using Megaphonk.Helpers;

namespace Megaphonk.Services
{
    /// <summary>
    /// Downed state: lethal damage downs the local player instead of killing it (invulnerable, no attacks, slow) while it
    /// bleeds out. A teammate standing next to it revives it. Bleeding out, or nobody left standing to revive, is a real
    /// death (grave revive as before). The downed player's machine decides; others are told through PlayerDownedChanged.
    /// </summary>
    public interface IDownedService
    {
        public bool IsLocalDowned { get; }

        /// <summary>
        /// The local player is about to die: true if it got downed instead
        /// </summary>
        public bool TryGoDown(PlayerHealth health);

        public void Tick();

        /// <summary>
        /// Dazed pose of downed players, after the animations and interpolation of the frame
        /// </summary>
        public void ApplyDownedPose();
        public void ResetForNextLevel();
        public void Reset();
    }

    internal class DownedService : IDownedService
    {
        private const float REVIVER_HINT_DISTANCE = 30f;
        private const float DAZED_LEAN_DEGREES = 25f;
        private const float DAZED_WOBBLE_DEGREES = 12f;
        private const float DAZED_WOBBLE_SPEED = 3f;

        /// <summary>
        /// Models currently posed as dazed: the rotation they would have without the pose, and the one we set
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<int, (Transform Transform, Quaternion BaseRotation, Quaternion PosedRotation)> dazedModels = [];

        private readonly IUdpClientService udpClientService;
        private readonly IPlayerManagerService playerManagerService;
        private readonly IPartySettingsService partySettingsService;
        private readonly IEncounterService encounterService;

        private bool allowNextDeath = false;
        private float bleedingFor = 0f;
        private float reviveProgress = 0f;
        private TextMeshProUGUI hudText;

        public bool IsLocalDowned { get; private set; } = false;

        public DownedService(IUdpClientService udpClientService, IPlayerManagerService playerManagerService, IPartySettingsService partySettingsService, IEncounterService encounterService)
        {
            this.udpClientService = udpClientService;
            this.playerManagerService = playerManagerService;
            this.partySettingsService = partySettingsService;
            this.encounterService = encounterService;

            EventManager.SubscribePlayerDownedChangedEvents(OnReceivedPlayerDownedChanged);
        }

        public bool TryGoDown(PlayerHealth health)
        {
            if (allowNextDeath)
            {
                allowNextDeath = false;
                return false;
            }

            var localHealth = GameManager.Instance?.player?.inventory?.playerHealth;
            var localPlayer = playerManagerService.GetLocalPlayer();
            if (!partySettingsService.Current.DownedEnabled || localHealth == null || localPlayer == null || health.Pointer != localHealth.Pointer)
            {
                return false;
            }

            if (IsLocalDowned)
            {
                health.hp = Math.Max(health.hp, 1); //Already down: stays down
                return true;
            }

            if (!GetOtherStandingPlayerIds(localPlayer.ConnectionId).Any())
            {
                return false; //Nobody left to revive
            }

            IsLocalDowned = true;
            bleedingFor = 0f;
            reviveProgress = 0f;
            health.hp = Math.Max(health.hp, 1);

            SetDowned(localPlayer.ConnectionId, true);
            Plugin.Log.LogInfo("Downed: bleeding out, a teammate can revive");
            return true;
        }

        public void Tick()
        {
            var localPlayer = playerManagerService.GetLocalPlayer();
            var player = GameManager.Instance?.player;
            if (localPlayer == null || player == null || player.inventory?.playerHealth == null)
            {
                return;
            }

            try
            {
                if (IsLocalDowned)
                {
                    TickDowned(localPlayer.ConnectionId, player);
                }
                else
                {
                    TickReviverHint(localPlayer.ConnectionId, player.transform.position);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Downed tick failed: {ex}");
            }
        }

        private void TickDowned(uint localPlayerId, MyPlayer player)
        {
            var settings = partySettingsService.Current;
            var health = player.inventory.playerHealth;
            health.hp = Math.Max(health.hp, 1);

            // Time.deltaTime keeps running while the game is paused: bleeding and reviving stop while a reward
            // (chest, level up, shrine...) is being picked or the party waits for someone else's pick
            var deltaTime = IsPickingPaused() ? 0f : Time.deltaTime;
            bleedingFor += deltaTime;
            var isTeammateNear = GetOtherStandingPlayerIds(localPlayerId)
                .Any(id => Vector3.Distance(GetPosition(id), player.transform.position) <= settings.DownedReviveRadius);
            reviveProgress = isTeammateNear ? reviveProgress + deltaTime : Math.Max(0f, reviveProgress - deltaTime);

            if (reviveProgress >= settings.DownedReviveSeconds)
            {
                Revive(localPlayerId, health);
                return;
            }

            var remaining = settings.DownedBleedoutSeconds - bleedingFor;
            if (remaining <= 0f || !GetOtherStandingPlayerIds(localPlayerId).Any())
            {
                BleedOut(localPlayerId, health);
                return;
            }

            ShowHud(isTeammateNear
                ? Loc.F("DOWN! Reviving... {0}", (reviveProgress / settings.DownedReviveSeconds).ToString("P0"))
                : Loc.F("DOWN! {0}s left\nA teammate must stand next to you", remaining.ToString("0")), Color.red);
        }

        private bool IsPickingPaused()
        {
            var encounterWindows = UiManager.Instance?.encounterWindows;
            return Assets.Scripts.Utility.MyTime.paused
                || encounterService.IsWaiting
                || (encounterWindows != null && encounterWindows.encounterInProgress);
        }

        private void TickReviverHint(uint localPlayerId, Vector3 localPosition)
        {
            var settings = partySettingsService.Current;
            var downedNearby = playerManagerService.GetAllPlayersExceptLocal()
                .Where(p => playerManagerService.IsDowned(p.ConnectionId))
                .Select(p => (p.Name, Distance: Vector3.Distance(GetPosition(p.ConnectionId), localPosition)))
                .Where(p => p.Distance <= REVIVER_HINT_DISTANCE)
                .OrderBy(p => p.Distance)
                .FirstOrDefault();

            if (downedNearby.Name == null)
            {
                HideHud();
                return;
            }

            ShowHud(downedNearby.Distance <= settings.DownedReviveRadius
                ? Loc.F("Reviving {0}...", downedNearby.Name)
                : Loc.F("{0} is down! {1}m", downedNearby.Name, downedNearby.Distance.ToString("0")), new Color(1f, 0.6f, 0.2f));
        }

        private void Revive(uint localPlayerId, PlayerHealth health)
        {
            IsLocalDowned = false;
            health.hp = Math.Max(1, Mathf.CeilToInt(health.maxHp * partySettingsService.Current.DownedReviveHpFraction));
            SetDowned(localPlayerId, false);
            HideHud();
            Plugin.Log.LogInfo("Revived by a teammate");
        }

        private void BleedOut(uint localPlayerId, PlayerHealth health)
        {
            IsLocalDowned = false;
            SetDowned(localPlayerId, false);
            HideHud();
            Plugin.Log.LogInfo("Bled out");

            allowNextDeath = true;
            health.hp = 0;
            health.PlayerDied();
        }

        private void SetDowned(uint playerId, bool downed)
        {
            playerManagerService.SetDowned(playerId, downed);

            IGameNetworkMessage message = new PlayerDownedChanged
            {
                PlayerId = playerId,
                Downed = downed
            };

            if (udpClientService.IsHost() ?? false)
            {
                udpClientService.SendToAllClients(message, LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
            else
            {
                udpClientService.SendToHost(message, LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
        }

        private void OnReceivedPlayerDownedChanged(PlayerDownedChanged message)
        {
            playerManagerService.SetDowned(message.PlayerId, message.Downed);
            Plugin.Log.LogInfo($"Player {message.PlayerId} {(message.Downed ? "is down" : "is back up")}");
        }

        /// <summary>
        /// Other players able to revive: alive and not downed
        /// </summary>
        private System.Collections.Generic.IEnumerable<uint> GetOtherStandingPlayerIds(uint localPlayerId)
        {
            return playerManagerService.GetAllPlayersAlive().Select(p => p.ConnectionId).Where(id => id != localPlayerId);
        }

        private Vector3 GetPosition(uint playerId)
        {
            if (playerManagerService.IsLocalConnectionId(playerId))
            {
                return GameManager.Instance.player.transform.position;
            }

            var netPlayer = playerManagerService.GetNetPlayerByNetplayId(playerId);
            return netPlayer?.Model != null ? netPlayer.Model.transform.position : new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        }

        private void ShowHud(string text, Color color)
        {
            if (hudText == null)
            {
                if (UiManager.Instance == null)
                {
                    return;
                }

                var go = new GameObject("Megaphonk_DownedHud");
                hudText = go.AddComponent<TextMeshProUGUI>();
                hudText.transform.SetParent(UiManager.Instance.transform, false);
                hudText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                hudText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                hudText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                hudText.rectTransform.anchoredPosition = new Vector2(0f, 220f);
                hudText.rectTransform.sizeDelta = new Vector2(900f, 120f);
                hudText.alignment = TextAlignmentOptions.Center;
                hudText.fontSize = 38;
                hudText.fontStyle = FontStyles.Bold;
                hudText.outlineColor = Color.black;
                hudText.outlineWidth = 0.2f;
            }

            hudText.enabled = true;
            hudText.text = text;
            hudText.color = color;
        }

        private void HideHud()
        {
            if (hudText != null)
            {
                hudText.enabled = false;
            }
        }

        public void ResetForNextLevel()
        {
            var health = GameManager.Instance?.player?.inventory?.playerHealth;
            if (IsLocalDowned && health != null)
            {
                health.hp = Math.Max(1, Mathf.CeilToInt(health.maxHp * partySettingsService.Current.DownedReviveHpFraction));
            }

            IsLocalDowned = false;
            allowNextDeath = false;
            bleedingFor = 0f;
            reviveProgress = 0f;
            HideHud();
        }

        public void Reset()
        {
            ResetForNextLevel();
        }

        // Reused every frame by ApplyDownedPose (LateUpdate): no per frame allocations
        private readonly System.Collections.Generic.List<Transform> downedModels = [];
        private readonly System.Collections.Generic.HashSet<int> posedIds = [];
        private readonly System.Collections.Generic.List<int> unposedIds = [];

        public void ApplyDownedPose()
        {
            downedModels.Clear();
            posedIds.Clear();

            var localRenderer = GameManager.Instance?.player?.playerRenderer;
            if (IsLocalDowned && localRenderer != null)
            {
                downedModels.Add(localRenderer.transform);
            }

            foreach (var netPlayer in playerManagerService.GetAllSpawnedNetPlayers())
            {
                if (netPlayer != null && netPlayer.Model != null && playerManagerService.IsDowned(netPlayer.ConnectionId))
                {
                    downedModels.Add(netPlayer.Model.transform);
                }
            }

            var wobblePhase = Time.unscaledTime * DAZED_WOBBLE_SPEED;
            var pose = Quaternion.Euler(
                DAZED_LEAN_DEGREES + Mathf.Sin(wobblePhase) * DAZED_WOBBLE_DEGREES,
                0f,
                Mathf.Cos(wobblePhase) * DAZED_WOBBLE_DEGREES);

            foreach (var model in downedModels)
            {
                var id = model.GetInstanceID();
                posedIds.Add(id);

                // Something else (animation, interpolation) set the rotation since our last pose: that is the new base
                var baseRotation = dazedModels.TryGetValue(id, out var posed) && model.rotation == posed.PosedRotation
                    ? posed.BaseRotation
                    : model.rotation;

                var posedRotation = baseRotation * pose;
                model.rotation = posedRotation;
                dazedModels[id] = (model, baseRotation, posedRotation);
            }

            unposedIds.Clear();
            foreach (var id in dazedModels.Keys)
            {
                if (!posedIds.Contains(id))
                {
                    unposedIds.Add(id);
                }
            }

            foreach (var id in unposedIds)
            {
                var posed = dazedModels[id];
                if (posed.Transform != null && posed.Transform.rotation == posed.PosedRotation)
                {
                    posed.Transform.rotation = posed.BaseRotation; //Back on its feet
                }
                dazedModels.Remove(id);
            }
        }
    }
}
