using Megaphonk.Common.Messages;
using Megaphonk.Common.Messages.GameNetworkMessages;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Megaphonk.Helpers;

namespace Megaphonk.Services
{
    /// <summary>
    /// Coop challenges, run by the host, rewarded with chests dropped from the sky when the next stage starts
    /// (opened by everyone in shared experience). Downs don't count as deaths.
    /// - a chest for each player who didn't die during the whole stage
    /// - a chest for each player who was alive when the stage boss appeared and didn't die before it was killed
    /// </summary>
    public interface IChallengeService
    {
        public void OnStageStarted();
        public void OnPlayerDied(uint playerId);
        public void OnStageBossSpawned();
        public void OnStageBossKilled(Vector3 position);
        public void OnStageEnded();
        public void Tick();
        public void Reset();
    }

    internal class ChallengeService : IChallengeService
    {
        private const float REWARD_CHEST_DELAY = 6f;

        private readonly IUdpClientService udpClientService;
        private readonly IPlayerManagerService playerManagerService;
        private readonly IPartySettingsService partySettingsService;

        private readonly HashSet<uint> diedThisStage = [];
        private HashSet<uint> bossFightSurvivors = null; //null: the boss hasn't appeared yet this stage
        private int pendingRewardChests = 0;
        private float spawnRewardsAt = float.MaxValue;
        private bool hasStageBossRewarded = false;

        public ChallengeService(IUdpClientService udpClientService, IPlayerManagerService playerManagerService, IPartySettingsService partySettingsService)
        {
            this.udpClientService = udpClientService;
            this.playerManagerService = playerManagerService;
            this.partySettingsService = partySettingsService;

            EventManager.SubscribeChallengeNoticeEvents(notice => ShowNotice(notice.Text));
        }

        private bool IsActive => (udpClientService.IsHost() ?? false) && partySettingsService.Current.ChallengesEnabled && (playerManagerService.GetAllPlayers().Count() > 1 || udpClientService.IsSoloSession());

        public void OnStageStarted()
        {
            diedThisStage.Clear();
            bossFightSurvivors = null;
            hasStageBossRewarded = false;
            if (!IsActive)
            {
                return;
            }

            if (pendingRewardChests > 0)
            {
                spawnRewardsAt = Time.time + REWARD_CHEST_DELAY;
            }

            Announce("Challenges: a chest for each player with no death this stage, and for each player who survives the boss fight");
        }

        public void OnPlayerDied(uint playerId)
        {
            bossFightSurvivors?.Remove(playerId);

            if (diedThisStage.Add(playerId) && IsActive)
            {
                Announce("Someone died: no stage chest for them this time");
            }
        }

        public void OnStageBossSpawned()
        {
            if (bossFightSurvivors != null)
            {
                return; //Only the first appearance starts the fight
            }

            bossFightSurvivors = [.. playerManagerService.GetAllPlayers()
                .Where(p => !playerManagerService.IsDead(p.ConnectionId))
                .Select(p => p.ConnectionId)];
        }

        public void OnStageBossKilled(Vector3 position)
        {
            if (!IsActive || hasStageBossRewarded)
            {
                return;
            }

            hasStageBossRewarded = true;

            // Boss appearance missed: whoever is alive now
            var survivors = (bossFightSurvivors ?? [.. playerManagerService.GetAllPlayers().Select(p => p.ConnectionId)])
                .Count(id => playerManagerService.GetPlayer(id) != null && !playerManagerService.IsDead(id));
            if (survivors == 0)
            {
                return;
            }

            pendingRewardChests += survivors;
            Announce("Boss defeated: a chest for each survivor of the fight awaits on the next stage!");
        }

        public void OnStageEnded()
        {
            if (!IsActive)
            {
                return;
            }

            var noDeathPlayers = playerManagerService.GetAllPlayers().Count(p => !diedThisStage.Contains(p.ConnectionId));
            if (noDeathPlayers == 0)
            {
                return;
            }

            pendingRewardChests += noDeathPlayers;
            Announce("Stage cleared: a chest for each player with no death awaits on the next stage!");
        }

        public void Tick()
        {
            if (pendingRewardChests <= 0 || Time.time < spawnRewardsAt || !(udpClientService.IsHost() ?? false))
            {
                return;
            }

            var player = GameManager.Instance?.player;
            if (player == null)
            {
                return;
            }

            try
            {
                // In a circle around the host, a wider one when there are many chests
                var radius = 3f + pendingRewardChests * 0.5f;
                for (var i = 0; i < pendingRewardChests; i++)
                {
                    var angle = i * Mathf.PI * 2f / pendingRewardChests;
                    SpawnRewardChest(player.transform.position + new Vector3(Mathf.Cos(angle) * radius, 2f, Mathf.Sin(angle) * radius));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not spawn challenge reward chests: {ex}");
            }

            pendingRewardChests = 0;
            spawnRewardsAt = float.MaxValue;
        }

        private static void SpawnRewardChest(Vector3 position)
        {
            // The host's chest spawns are synchronized to the clients by the Object.Instantiate patch
            EffectManager.Instance.SpawnChest(EffectManager.Instance.openChestNormal, position);
        }

        private void Announce(string text)
        {
            IGameNetworkMessage message = new ChallengeNotice { Text = text };
            udpClientService.SendToAllClients(message, LiteNetLib.DeliveryMethod.ReliableOrdered);
            ShowNotice(text);
        }

        private static void ShowNotice(string text)
        {
            Plugin.Log.LogInfo(text);
            Plugin.StartNotification(("Megaphonk", "Challenge"), ("Megaphonk", "Challenge_Description"), [Loc.T(text)]);
        }

        public void Reset()
        {
            diedThisStage.Clear();
            bossFightSurvivors = null;
            pendingRewardChests = 0;
            spawnRewardsAt = float.MaxValue;
            hasStageBossRewarded = false;
        }
    }
}
