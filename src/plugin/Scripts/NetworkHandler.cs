using Assets.Scripts._Data.MapsAndStages;
using Assets.Scripts.Managers;
using Il2CppInterop.Runtime;
using Megaphonk.Configuration;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Megaphonk.Scripts
{
    public class NetworkHandler : MonoBehaviour
    {
        private const float LOBBY_UPDATE_TICK_RATE = 60f;
        private const float lobbyUpdatetickInterval = 1f / LOBBY_UPDATE_TICK_RATE;
        private float lobbyUpdateAccumulator = 0f;

        // Near enemies are sent every tick they move and interpolated 0.1 s behind on clients (4 ticks, as 1.0.0); mid/far ones
        // are read every 4th/8th tick (EnemySnapshotPlanner), clients extrapolate in between (dead reckoning).
        // Counted in unscaled time: keyframes and the "stop" of a pause still go out when the time scale is 0
        private const float ENEMY_UPDATE_TICK_RATE = 40f;
        private const float enemyUpdatetickInterval = 1f / ENEMY_UPDATE_TICK_RATE;
        private float enemyUpdateAccumulator = 0f;

        private const float PROJECTILE_UPDATE_TICK_RATE = 20f;
        private const float projectileUpdatetickInterval = 1f / PROJECTILE_UPDATE_TICK_RATE;
        private float projectileUpdateAccumulator = 0f;

        private const float TUMBLEWEED_UPDATE_TICK_RATE = 20f;
        private const float tumbleWeedUpdatetickInterval = 1f / TUMBLEWEED_UPDATE_TICK_RATE;
        private float tumbleWeedUpdateAccumulator = 0f;

        private bool hasStarted = false;
        private bool? hasFoundMatch = null;
        private bool? hasJoinedFriendlyRoom = null;
        private bool? isConnectedToMatchMaker = null;
        private bool IsNetworkInterrupted = false;
        private string matchMakerFailureMessage = string.Empty;

        private bool isHost = false;
        private bool isGameStarted = false;

        private IUdpClientService udpClientService;
        private ISynchronizationService synchronizationService;
        private IWebsocketClientService websocketClientService;
        private IPlayerManagerService playerManagerService;
        private IPartyStatsService partyStatsService;
        private IPartySettingsService partySettingsService;
        private IPartyModifiersService partyModifiersService;
        private IDownedService downedService;
        private IPingService pingService;
        private ITeamMeterService teamMeterService;
        private ICurseService curseService;
        private IChallengeService challengeService;
        private IRunStatsService runStatsService;
        private ICoopLeaderboardService coopLeaderboardService;

        public bool? IsConnectedToMatchMaker => isConnectedToMatchMaker;
        public string MatchMakerFailureMessage => matchMakerFailureMessage;
        public bool? HasFoundMatch => hasFoundMatch;

        public bool? HasJoinedFriendlyRoom => hasJoinedFriendlyRoom;
        public bool IsNetworkInterruptedStatus => IsNetworkInterrupted;

        public bool IsHost => isHost;

        /// <summary>
        /// Per frame values read once here instead of once per enemy/interpolator (each Time call is a native call).
        /// Scripts updated before this one in a frame see the previous frame's values.
        /// </summary>
        public static int FrameIndex { get; private set; }
        public static double FrameTimeAsDouble { get; private set; }
        public static float FrameDeltaTime { get; private set; }
        public static float FrameUnscaledDeltaTime { get; private set; }
        /// <summary>
        /// The game is paused (party pause, menus: MyTime) or its time scale is 0 (hit stop): enemies don't move
        /// </summary>
        public static bool IsGamePaused { get; private set; }
        /// <summary>
        /// Game time for enemy dead reckoning: stops while IsGamePaused, on the host and on clients alike
        /// </summary>
        public static double EnemyClock { get; private set; }

        // Created once: a method group passed to SafeTick allocated a delegate per call, every frame
        private System.Action[] updateTicks;
        private System.Action flushPendingNetworkUpdatesTick;
        private System.Action applyDownedPoseTick;

        public void Awake()
        {
            websocketClientService = Plugin.Services.GetRequiredService<IWebsocketClientService>();
            playerManagerService = Plugin.Services.GetRequiredService<IPlayerManagerService>();

            EventManager.SubscribeGameStartedEvents(OnGameStarted);
            EventManager.SubscribePortalOpenedEvents(OnPortalOpened);
        }

        private void OnPortalOpened()
        {
            isGameStarted = false;
            udpClientService?.ForceFullInventories();
        }

        private void OnGameStarted()
        {
            isGameStarted = true;
            udpClientService?.ForceFullInventories(); //Level changed: everyone gets the full inventories again
        }

        public void Update()
        {
            FrameIndex++;
            FrameTimeAsDouble = Time.timeAsDouble;
            FrameDeltaTime = Time.deltaTime;
            FrameUnscaledDeltaTime = Time.unscaledDeltaTime;
            PerfProbe.OnFrame(FrameUnscaledDeltaTime);
            IsGamePaused = Assets.Scripts.Utility.MyTime.paused || FrameDeltaTime <= 0f;
            if (!IsGamePaused)
            {
                EnemyClock += FrameDeltaTime;
            }

            try
            {
                if (udpClientService == null || synchronizationService == null) return;

                if (hasFoundMatch == null) return;

                if (!hasFoundMatch.HasValue && !hasFoundMatch.Value || synchronizationService.IsLoadingNextLevel()) return;

                var perfPoll = PerfProbe.Begin();
                udpClientService.Poll();
                PerfProbe.End("Net.Poll", perfPoll);

                if (GameManager.Instance == null || GameManager.Instance.player == null || GameManager.Instance.player.inventory == null) return;

                // Each feature on its own: one failing must not stop the player/enemy updates below
                updateTicks ??=
                [
                    synchronizationService.UpdateEncounterSync,
                    synchronizationService.RetryPendingInventoryReplays,
                    synchronizationService.CheckPeerModVersions,
                    partyStatsService.Tick,
                    partyModifiersService.Tick,
                    downedService.Tick,
                    pingService.Tick,
                    teamMeterService.Tick,
                    curseService.Tick,
                    challengeService.Tick,
                    runStatsService.Tick,
                    DebugTools.Tick,
                ];
                foreach (var tick in updateTicks)
                {
                    SafeTick(tick);
                }

                var deltaTime = FrameDeltaTime;
                lobbyUpdateAccumulator += deltaTime;

                if (isHost && isGameStarted)
                {
                    enemyUpdateAccumulator += FrameUnscaledDeltaTime;
                    projectileUpdateAccumulator += deltaTime;

                    if (MapController.runConfig.mapData.eMap == EMap.Desert)
                    {
                        tumbleWeedUpdateAccumulator += deltaTime;
                    }
                }

                // UiManager.Instance.GetComponentInChildren<TargetOfInterestUi>().RefreshPrefabs();

                // Run each tick at most once per frame and drop the backlog. Catching up on missed ticks after a slow frame
                // sent several full snapshots in a row, making the next frame even slower (late game FPS spiral).
                // Snapshots carry the latest state, so skipped ticks lose nothing.
                if (lobbyUpdateAccumulator >= lobbyUpdatetickInterval)
                {
                    lobbyUpdateAccumulator = Mathf.Min(lobbyUpdateAccumulator - lobbyUpdatetickInterval, lobbyUpdatetickInterval);
                    var perfUpdate = PerfProbe.Begin();
                    udpClientService.Update();
                    PerfProbe.End("Net.Update", perfUpdate);
                }

                if (isHost && enemyUpdateAccumulator >= enemyUpdatetickInterval)
                {
                    enemyUpdateAccumulator = Mathf.Min(enemyUpdateAccumulator - enemyUpdatetickInterval, enemyUpdatetickInterval);
                    var perfEnemies = PerfProbe.Begin();
                    udpClientService.UpdateEnemies();
                    PerfProbe.End("Net.UpdateEnemies", perfEnemies);
                }

                if (isHost && projectileUpdateAccumulator >= projectileUpdatetickInterval)
                {
                    projectileUpdateAccumulator = Mathf.Min(projectileUpdateAccumulator - projectileUpdatetickInterval, projectileUpdatetickInterval);
                    var perfProjectiles = PerfProbe.Begin();
                    udpClientService.UpdateProjectiles();
                    PerfProbe.End("Net.UpdateProjectiles", perfProjectiles);
                }

                if (isHost && tumbleWeedUpdateAccumulator >= tumbleWeedUpdatetickInterval)
                {
                    tumbleWeedUpdateAccumulator = Mathf.Min(tumbleWeedUpdateAccumulator - tumbleWeedUpdatetickInterval, tumbleWeedUpdatetickInterval);
                    var perfTumbleWeeds = PerfProbe.Begin();
                    udpClientService.UpdateTumbleWeeds();
                    PerfProbe.End("Net.UpdateTumbleWeeds", perfTumbleWeeds);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"NetworkHandler Update error: {ex}");
            }
        }

        /// <summary>
        /// End of frame: send what was merged or queued during the frame (damage, xp, then every queued message in batches)
        /// </summary>
        public void LateUpdate()
        {
            try
            {
                if (udpClientService == null) return;

                if (synchronizationService != null)
                {
                    flushPendingNetworkUpdatesTick ??= synchronizationService.FlushPendingNetworkUpdates;
                    SafeTick(flushPendingNetworkUpdatesTick);
                }

                var perfFlush = PerfProbe.Begin();
                udpClientService.FlushPendingMessages();
                PerfProbe.End("Net.FlushPendingMessages", perfFlush);

                if (downedService != null && GameManager.Instance != null && GameManager.Instance.player != null)
                {
                    applyDownedPoseTick ??= downedService.ApplyDownedPose;
                    SafeTick(applyDownedPoseTick);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"NetworkHandler LateUpdate error: {ex}");
            }
        }

        private static void SafeTick(System.Action tick)
        {
            var perf = PerfProbe.Begin();
            try
            {
                tick();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"{tick.Method.DeclaringType?.Name}.{tick.Method.Name} failed: {ex}");
            }
            PerfProbe.End(tick.Method, perf);
        }

        public int GetLobbySize()
        {
            return playerManagerService.GetAllPlayers().Count();
        }

        public void HandleNetworking()
        {
            try
            {
                Task webSocket = new Task(async () =>
                {
                    try
                    {
                        IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());

                        hasFoundMatch = null;
                        IsNetworkInterrupted = false;
                        matchMakerFailureMessage = string.Empty;
                        if (isConnectedToMatchMaker.HasValue && !isConnectedToMatchMaker.Value)
                        {
                            isConnectedToMatchMaker = null;
                        }

                        await websocketClientService.ConnectAndMatchAsync(ModConfig.ServerUrl.Value, ModConfig.RDVServerPort.Value, this);
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"WebSocket connection error: {ex.Message}");
                        isConnectedToMatchMaker = false;
                        matchMakerFailureMessage = ex.Message;
                    }
                });

                webSocket.Start();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"WebSocket task error: {ex}");
            }
        }

        public void ResetNetworking()
        {
            isConnectedToMatchMaker = null;
            Plugin.Instance.Mode = new();
            isHost = false;

            try
            {
                (udpClientService ?? Plugin.Services.GetRequiredService<IUdpClientService>()).Reset();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Error resetting UDP client: {ex}");
            }
            finally
            {
                udpClientService = null;
            }

            try
            {
                synchronizationService?.Reset();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Error resetting Synchronization service: {ex}");
            }
            finally
            {
                synchronizationService = null;
            }

            playerManagerService?.Reset();
            partyStatsService?.Reset();
            partySettingsService?.Reset();
            partyModifiersService?.Reset();
            downedService?.Reset();
            pingService?.Reset();
            teamMeterService?.Reset();
            curseService?.Reset();
            challengeService?.Reset();
            runStatsService?.Reset();
            coopLeaderboardService?.Reset();

            if (websocketClientService != null)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await websocketClientService.Reset();
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"Error resetting websocket: {ex}");
                    }
                }).Wait();
            }
        }

        public void OnConnectedToMatchMaker()
        {
            isConnectedToMatchMaker = true;
        }

        public void OnFailedToConnectToMatchMaker(string message)
        {
            isConnectedToMatchMaker = false;
            matchMakerFailureMessage = message;
        }

        public void OnNetworkInterrupted(string message)
        {
            IsNetworkInterrupted = true;
            matchMakerFailureMessage = message;
        }

        public void OnMatchFound(bool success)
        {
            hasFoundMatch = success;
            if (success)
            {
                udpClientService = Plugin.Services.GetRequiredService<IUdpClientService>();
                synchronizationService = Plugin.Services.GetRequiredService<ISynchronizationService>();
                partyStatsService = Plugin.Services.GetRequiredService<IPartyStatsService>();
                partySettingsService = Plugin.Services.GetRequiredService<IPartySettingsService>();
                partyModifiersService = Plugin.Services.GetRequiredService<IPartyModifiersService>();
                downedService = Plugin.Services.GetRequiredService<IDownedService>();
                pingService = Plugin.Services.GetRequiredService<IPingService>();
                teamMeterService = Plugin.Services.GetRequiredService<ITeamMeterService>();
                curseService = Plugin.Services.GetRequiredService<ICurseService>();
                challengeService = Plugin.Services.GetRequiredService<IChallengeService>();
                runStatsService = Plugin.Services.GetRequiredService<IRunStatsService>();
                coopLeaderboardService = Plugin.Services.GetRequiredService<ICoopLeaderboardService>();
                if (Plugin.Instance.Mode.Mode == Common.Models.NetworkModeType.Random)
                {
                    isHost = synchronizationService.IsServerMode() ?? false;
                }
                else
                {
                    isHost = Plugin.Instance.Mode.Role == Common.Models.Role.Host;
                    udpClientService.UpdateMode(isHost);
                }
            }
            else
            {
                if (Plugin.Instance.Mode.Mode == Common.Models.NetworkModeType.Random)
                {
                    matchMakerFailureMessage = "Failed to establish P2P connection.";
                }
                IsNetworkInterrupted = true;
            }
        }

    }
}
