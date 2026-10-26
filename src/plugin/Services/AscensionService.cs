using Assets.Scripts.Managers;
using Megaphonk.Common.Messages;
using Megaphonk.Common.Messages.GameNetworkMessages;
using Megaphonk.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections;
using UnityEngine;

namespace Megaphonk.Services
{
    /// <summary>
    /// Co-op endgame: next to the final portal (which still ends the run, unlocks included) stands an ascension portal that
    /// takes the party back to the first stage of the same map with their builds (ascension 1, 2, ...). Each level makes
    /// enemies stronger (PartyModifiersService), never more numerous (GameBalanceService caps them at
    /// PartySettings.AscensionMaxEnemies). The host decides: a client's use is a request, the host's AscensionStarted
    /// then takes everyone through the regular next stage transition.
    /// </summary>
    public interface IAscensionService
    {
        public int Level { get; }
        public bool IsAvailable { get; }

        /// <summary>
        /// An ascension was decided and the party is on its way to the first stage
        /// </summary>
        public bool IsStarting { get; }

        /// <summary>
        /// The ascension portal goes next to the final portal (every machine of a co-op game, whatever the moment)
        /// </summary>
        public bool CanPlacePortal { get; }

        /// <summary>
        /// The local player used the ascension portal
        /// </summary>
        public void UsePortal();
        public void Reset();
    }

    internal class AscensionService : IAscensionService
    {
        private const float LOAD_DELAY = 2f; //InteractablePortal.DoLoadNextStage waits as long after the teleport

        private readonly IUdpClientService udpClientService;
        private readonly IPartySettingsService partySettingsService;
        private readonly ICurseService curseService;
        // Resolved when used: the synchronization service depends on services that depend on this one
        private readonly Lazy<ISynchronizationService> synchronization = new(() => Plugin.Services.GetService<ISynchronizationService>());
        private bool starting;

        public int Level { get; private set; }

        public bool IsAvailable => synchronization.Value.HasNetplaySessionStarted() && CanPlacePortal;

        public bool IsStarting => starting;

        public bool CanPlacePortal => udpClientService.IsHost().HasValue && partySettingsService.Current.AscensionEnabled;

        private bool IsHost => udpClientService.IsHost() ?? false;

        public AscensionService(IUdpClientService udpClientService, IPartySettingsService partySettingsService, ICurseService curseService)
        {
            this.udpClientService = udpClientService;
            this.partySettingsService = partySettingsService;
            this.curseService = curseService;

            // On the host a client's request (its Level is unused), on a client the host's decision
            EventManager.SubscribeAscensionStartedEvents(message =>
            {
                if (IsHost)
                {
                    StartNext();
                }
                else if (message.Level > Level && !synchronization.Value.IsEndgame())
                {
                    Start(message.Level); //Not an old one again, nor after this player's own final portal ended the run
                }
            });
        }

        public void UsePortal()
        {
            if (!IsAvailable)
            {
                return;
            }

            if (IsHost)
            {
                StartNext();
            }
            else
            {
                udpClientService.SendToHost(new AscensionStarted(), LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
        }

        private void StartNext()
        {
            // Both players used the portal, a request that came after the ascension (handled once loaded) or from
            // anywhere but the final boss stage: the ascension already started or can't
            if (starting || !IsAvailable || !MapController.isFinalBossStage)
            {
                return;
            }

            var level = Level + 1;
            udpClientService.SendToAllClients(new AscensionStarted { Level = level }, LiteNetLib.DeliveryMethod.ReliableOrdered);
            Start(level);
        }

        /// <summary>
        /// Next frame, out of the interaction that triggered it
        /// </summary>
        private void Start(int level)
        {
            if (starting)
            {
                return;
            }

            starting = true;
            CoroutineRunner.Instance.Run(StartRoutine(level));
        }

        private IEnumerator StartRoutine(int level)
        {
            yield return null;

            if (synchronization.Value.IsEndgame() && (udpClientService.IsHost() ?? false))
            {
                starting = false;
                yield break; //The final portal won meanwhile: the run ends
            }

            try
            {
                Level = level;
                var settings = partySettingsService.Current;
                Plugin.Log.LogInfo($"Ascension {level}: back to the first stage, enemy HP x{Math.Pow(settings.AscensionEnemyHpGrowth, level):0.##}, damage x{Math.Pow(settings.AscensionEnemyDamageGrowth, level):0.##}, at most {settings.AscensionMaxEnemies} enemies");

                // LoadNextStage loads the stage at ++index: the first one
                MapController.index = -1;
                MapController.isFinalBossStage = false;
                curseService.QueueChoice(level); //Each player picks a curse on the first stage

                Plugin.CAN_SEND_MESSAGES = false;
                synchronization.Value.TransitionToState(GameEvent.PortalOpened);
                GameManager.Instance.player.TeleportPlayerNextStage();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Ascension {level} failed: {ex}");
            }
            finally
            {
                Plugin.CAN_SEND_MESSAGES = true;
            }

            // As the game's portal does: the stage loads once the teleport animation played
            var loadAt = Time.realtimeSinceStartup + LOAD_DELAY;
            while (Time.realtimeSinceStartup < loadAt)
            {
                yield return null;
            }

            try
            {
                MapController.LoadNextStage();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Ascension {level}: loading the first stage failed: {ex}");
            }
            finally
            {
                starting = false;
            }

            Plugin.StartNotification(("Megaphonk", "Ascension"), ("Megaphonk", "Ascension_Description"), [level.ToString()]);
        }

        public void Reset()
        {
            Level = 0;
            starting = false;
            curseService.Reset();
        }
    }
}
