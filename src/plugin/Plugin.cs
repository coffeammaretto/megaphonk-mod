using Assets.Scripts.Game.MapGeneration.MapEvents;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Menu.Shop;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using MegabonkTogether.Common;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Configuration;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts;
using MegabonkTogether.Scripts.Button;
using MegabonkTogether.Scripts.Enemies;
using MegabonkTogether.Scripts.Interactables;
using MegabonkTogether.Scripts.Modal;
using MegabonkTogether.Scripts.NetPlayer;
using MegabonkTogether.Scripts.Snapshot;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace MegabonkTogether
{
    public enum DistanceToPlayer
    {
        Close,
        Medium,
        Far
    }

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        public static Plugin Instance = null!;
        public NetworkHandler NetworkHandler = null;
        public Dictionary<ECharacter, RawImage> CharactersIcon = [];
        public NetPlayersDisplayer NetPlayersDisplayer = null;
        public CameraSwitcher CameraSwitcher = null;
        public PlayTogetherButton PlayTogetherButton = null;
        public AchievementPopup AchievementPopup = null;
        public NotificationQueueManager NotificationQueueManager = null;
        private MainMenu MainMenu = null;
        private MapEventsManager mapEventsManager = null;
        private MapEventsDesert mapEventsDesert = null;
        private LoadingModal modal;
        public NetworkMode Mode = new();

        public static IHost Host = null!;
        public static IServiceProvider Services => Host.Services;

        internal NetworkMenuTab NetworkTab { get; set; }

        internal static new ManualLogSource Log;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private CancellationToken cancellationToken;
        public static float PLAYER_FEET_OFFSET_Y = 1.50f;

        public static bool CAN_SPAWN_PICKUPS = false;
        public static bool CAN_SPAWN_CHESTS = false;
        public static bool CAN_SEND_MESSAGES = true;
        public static bool CAN_ENEMY_EXPLODE = false;
        public static bool CAN_ENEMY_USE_SPECIAL_ATTACK = false;
        public bool CAN_SPAWN_TORNADOES = false;
        public bool CAN_START_STOP_STORMS = false;
        public IntPtr DAMAGE_BEING_REPLAYED = IntPtr.Zero; //Client: DamageContainer received from the host, the only one applied locally
        public bool IS_HOST_READY = false;
        public bool IS_MANUAL_INVINCIBLE = false;
        public bool IS_NETPLAYER_ADDING_TOME = false;

        public uint? CurrentReviver = null;
        public uint? CurrentReviverOwner = null;

        private Vector3 WorldSize = Vector3.zero;
        public Vector3 OriginalWorldSize = Vector3.zero;
        public bool HasDungeonTimerStarted = false;

        private readonly ConcurrentDictionary<string, GameObject> prefabs = new();

        private Il2CppSystem.Action originalDiedAction = null;
        private Il2CppSystem.Action preventedDiedAction = null;
        private bool isDeathPrevented = false;
        private Il2CppSystem.Action<WeaponBase> originalWeaponAddedAction = null;
        private Il2CppSystem.Action<EStat> originalStatUpdateAction = null;

        private static int distanceTargetFrame = -1;
        private static Vector3 distanceTarget;

        /// <summary>
        /// The BepInEx console is on by default and every line written to it blocks the main thread. Turned off in the BepInEx
        /// config, it applies from the next launch (the console of this launch is already open).
        /// </summary>
        private static void DisableBepInExConsole()
        {
            if (ModConfig.KeepBepInExConsole.Value)
            {
                return;
            }

            try
            {
                if (BepInEx.Configuration.ConfigFile.CoreConfig.TryGetEntry<bool>("Logging.Console", "Enabled", out var consoleEnabled) && consoleEnabled.Value)
                {
                    consoleEnabled.Value = false;
                    BepInEx.Configuration.ConfigFile.CoreConfig.Save();
                    Log.LogInfo("BepInEx console turned off from the next launch (Diagnostics.KeepBepInExConsole to keep it)");
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Could not turn off the BepInEx console: {ex.Message}");
            }
        }

        public override void Load()
        {
            Instance = this;
            cancellationToken = cancellationTokenSource.Token;

            Log = base.Log;
            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            ModConfig.Initialize(Config);
            Log.LogInfo($"Player name set to: {ModConfig.PlayerName.Value}");
            DisableBepInExConsole();

            ClassInjector.RegisterTypeInIl2Cpp<NetPlayer>();
            ClassInjector.RegisterTypeInIl2Cpp<CoroutineRunner>();
            ClassInjector.RegisterTypeInIl2Cpp<MainThreadDispatcher>();
            ClassInjector.RegisterTypeInIl2Cpp<NetworkHandler>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerInterpolator>();
            ClassInjector.RegisterTypeInIl2Cpp<EnemyInterpolator>();
            ClassInjector.RegisterTypeInIl2Cpp<BossOrbInterpolator>();
            ClassInjector.RegisterTypeInIl2Cpp<ProjectileInterpolator>();
            ClassInjector.RegisterTypeInIl2Cpp<TumbleWeedInterpolator>();
            ClassInjector.RegisterTypeInIl2Cpp<NetPlayersDisplayer>();
            ClassInjector.RegisterTypeInIl2Cpp<NetPlayerCard>();
            ClassInjector.RegisterTypeInIl2Cpp<DisplayBar>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomInventoryHud>();
            ClassInjector.RegisterTypeInIl2Cpp<CameraSwitcher>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayTogetherButton>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomButton>();
            ClassInjector.RegisterTypeInIl2Cpp<ModalBase>();
            ClassInjector.RegisterTypeInIl2Cpp<NetworkMenuTab>();
            ClassInjector.RegisterTypeInIl2Cpp<NetplayOptionsModal>();
            ClassInjector.RegisterTypeInIl2Cpp<LoadingModal>();
            ClassInjector.RegisterTypeInIl2Cpp<UpdateAvailableModal>();
            ClassInjector.RegisterTypeInIl2Cpp<ChangelogModal>();
            ClassInjector.RegisterTypeInIl2Cpp<CoopLeaderboardPanel>();
            ClassInjector.RegisterTypeInIl2Cpp<TargetSwitcher>();
            ClassInjector.RegisterTypeInIl2Cpp<InteractableReviver>();
            ClassInjector.RegisterTypeInIl2Cpp<NotificationQueueManager>();

            var builder = new HostBuilder();

            string contentRoot = System.IO.Directory.GetCurrentDirectory();
            builder.UseContentRoot(contentRoot);

            builder.ConfigureServices(services =>
            {
                services.AddSingleton(Log);
                services.AddSingleton<WebsocketClientService>();
                services.AddSingleton<Steam.ISteamTransport, Steam.SteamTransport>();
                services.AddSingleton<IWebsocketClientService, Steam.SteamMatchmakingService>();

                services.AddSingleton<IUdpClientService, UdpClientService>();
                services.AddSingleton<IPlayerManagerService, PlayerManagerService>();
                services.AddSingleton<IEnemyManagerService, EnemyManagerService>();
                services.AddSingleton<IProjectileManagerService, ProjectileManagerService>();
                services.AddSingleton<ISynchronizationService, SynchronizationService>();
                services.AddSingleton<IPickupManagerService, PickupManagerService>();
                services.AddSingleton<IChestManagerService, ChestManagerService>();
                services.AddSingleton<ISpawnedObjectManagerService, SpawnedObjectManagerService>();
                services.AddSingleton<IFinalBossOrbManagerService, FinalBossOrbManagerService>();
                services.AddSingleton<ILocalizationService, LocalizationService>();
                services.AddSingleton<IGameBalanceService, GameBalanceService>();
                services.AddSingleton<IAutoUpdaterService, AutoUpdaterService>();
                services.AddSingleton<IChangelogService, ChangelogService>();
                services.AddSingleton<IEncounterService, EncounterService>();
                services.AddSingleton<IPartyStatsService, PartyStatsService>();
                services.AddSingleton<IPartySettingsService, PartySettingsService>();
                services.AddSingleton<IPartyModifiersService, PartyModifiersService>();
                services.AddSingleton<IDownedService, DownedService>();
                services.AddSingleton<IPingService, PingService>();
                services.AddSingleton<ITeamMeterService, TeamMeterService>();
                services.AddSingleton<IChallengeService, ChallengeService>();
                services.AddSingleton<IRunStatsService, RunStatsService>();
                services.AddSingleton<ICoopLeaderboardService, CoopLeaderboardService>();
                services.AddSingleton<ITrackerService, TrackerService>();
            });

            Host = builder.Build();


            _ = Services.GetRequiredService<ISynchronizationService>(); // Initialize SynchronizationService
            _ = Host.StartAsync(cancellationToken);
            var autoUpdaterService = Services.GetRequiredService<IAutoUpdaterService>();

            if (ModConfig.CheckForUpdates.Value)
            {
                autoUpdaterService.Initialize();

                Task.Run(async () =>
                {
                    try
                    {
                        var updateAvailable = await autoUpdaterService.CheckAndUpdate();
                        if (updateAvailable && !autoUpdaterService.IsCustomBuild())
                        {
                            Log.LogInfo("An update has been downloaded and will be applied when you quit the game.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogError($"Auto-update check failed: {ex.Message}");
                    }
                });
            }
            else
            {
                Log.LogInfo("Auto-update is disabled in configuration.");
            }

            // Patch each class separately so a single broken target (e.g. after a game update)
            // does not silently skip every patch that comes after it.
            var harmony = new HarmonyLib.Harmony(MyPluginInfo.PLUGIN_GUID);
            foreach (var type in HarmonyLib.AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception ex)
                {
                    Log.LogError($"Harmony patching failed for {type.FullName}: {ex}");
                }
            }

            if (!ModConfig.NativeTransformHooks.Value || !Patches.Unity.TransformDetours.TryApply())
            {
                Patches.Unity.TransformDetours.ApplyHarmonyFallback(harmony);
            }

            if (ModConfig.PerformanceLog.Value)
            {
                try
                {
                    PerfProbe.Initialize(harmony, Log, ModConfig.PerformanceSpikeMs.Value);
                }
                catch (Exception ex)
                {
                    Log.LogError($"Performance log setup failed: {ex}");
                }
            }

            var go = new GameObject("MainThreadDispatcher");
            GameObject.DontDestroyOnLoad(go);
            go.AddComponent<MainThreadDispatcher>();

            var goNetworkHandler = new GameObject("NetworkHandler");
            GameObject.DontDestroyOnLoad(goNetworkHandler);
            NetworkHandler = goNetworkHandler.AddComponent<NetworkHandler>();

            var goNetPlayersDisplayer = new GameObject("NetPlayersDisplayer");
            GameObject.DontDestroyOnLoad(goNetPlayersDisplayer);
            NetPlayersDisplayer = goNetPlayersDisplayer.AddComponent<NetPlayersDisplayer>();

            var goCameraSwitcher = new GameObject("CameraSwitcher");
            GameObject.DontDestroyOnLoad(goCameraSwitcher);
            CameraSwitcher = goCameraSwitcher.AddComponent<CameraSwitcher>();

            var goNotificationQueueManager = new GameObject("NotificationQueueManager");
            GameObject.DontDestroyOnLoad(goNotificationQueueManager);
            NotificationQueueManager = goNotificationQueueManager.AddComponent<NotificationQueueManager>();
        }

        public void AddPrefab(GameObject prefab)
        {
            prefabs.TryAdd(prefab.name, prefab);
        }

        public GameObject GetPrefab(string name)
        {
            if (prefabs.TryGetValue(name.Trim(), out var prefab))
            {
                return prefab;
            }

            Plugin.Log.LogWarning($"Prefab not found: {name}");

            return null;
        }

        public void ClearPrefabs()
        {
            prefabs.Clear();
        }

        public void ClearCharacterIcons()
        {
            CharactersIcon.Clear();
        }

        public void AddCharacterIcons(Il2CppSystem.Collections.Generic.List<MyButtonCharacter> characterButtons)
        {
            if (CharactersIcon.Count > 0)
            {
                Log.LogWarning("Character icons already added");
                return;
            }

            foreach (var button in characterButtons)
            {
                var iconObj = UnityEngine.Object.Instantiate(button.i_icon);
                UnityEngine.Object.DontDestroyOnLoad(iconObj);
                CharactersIcon.Add(button.characterData.eCharacter, iconObj);
            }

        }

        public void PreventDeath()
        {
            if (isDeathPrevented)
            {
                Log.LogWarning("Death already prevented");
                return;
            }

            originalDiedAction = PlayerHealth.A_Died;
            PlayerHealth.A_Died = new Action(OnPlayerDied);
            preventedDiedAction = PlayerHealth.A_Died;
            isDeathPrevented = true;
        }

        private void OnPlayerDied()
        {
            if (CameraSwitcher == null || CameraSwitcher.IsFollowingTarget)
            {
                return;
            }

            var playerManager = Services.GetService<IPlayerManagerService>();
            var netPlayer = playerManager.GetRandomNetPlayer();

            if (netPlayer != null)
            {
                CameraSwitcher.SwitchToTarget(netPlayer.ConnectionId);
            }
        }

        public void RestoreDeath(bool invokeDeathEvent)
        {
            if (!isDeathPrevented)
            {
                Log.LogWarning("Death not prevented");
                return;
            }

            CameraSwitcher.ResetToLocalPlayer();

            // Keep the handlers the game subscribed while the death was prevented (they went into our action)
            Il2CppSystem.Delegate addedDuringRun = PlayerHealth.A_Died != null && preventedDiedAction != null
                ? Il2CppSystem.Delegate.Remove(PlayerHealth.A_Died, preventedDiedAction)
                : PlayerHealth.A_Died;
            var restored = Il2CppSystem.Delegate.Combine(originalDiedAction, addedDuringRun);
            PlayerHealth.A_Died = restored?.Cast<Il2CppSystem.Action>();
            originalDiedAction = null;
            preventedDiedAction = null;
            isDeathPrevented = false;

            if (invokeDeathEvent)
            {
                GameManager.Instance.player.playerRenderer.gameObject.SetActive(true);
                InvokeDiedHandlers();
            }
        }

        /// <summary>
        /// Invoke the death handlers one by one: a handler of an object destroyed since it subscribed (its unsubscription
        /// missed the event while it was replaced, e.g. a run left to the menu) throws, and stopped every handler after it,
        /// the game over screen too. Such a handler is dropped from the event, the game invokes it again on each death.
        /// </summary>
        private void InvokeDiedHandlers()
        {
            var died = PlayerHealth.A_Died;
            if (died == null)
            {
                return;
            }

            foreach (var handler in died.GetInvocationList())
            {
                try
                {
                    handler.Cast<Il2CppSystem.Action>().Invoke();
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Dropped a failing player death handler: {ex.Message}");
                    var remaining = Il2CppSystem.Delegate.Remove(PlayerHealth.A_Died, handler);
                    PlayerHealth.A_Died = remaining?.Cast<Il2CppSystem.Action>();
                }
            }
        }

        public AchievementPopup GetAchievementPopup()
        {
            if (AchievementPopup == null)
            {
                var gameObject = Il2CppFindHelper.FindAllGameObjects()
                    .FirstOrDefault(go => go.GetComponent<AchievementPopup>() != null);
                if (gameObject != null)
                {
                    AchievementPopup = gameObject.GetComponent<AchievementPopup>();

                    if (AchievementPopup != null && NotificationQueueManager != null)
                    {
                        NotificationQueueManager.Initialize();
                    }
                }
            }
            return AchievementPopup;
        }

        public MainMenu GetMainMenu()
        {
            return MainMenu;
        }

        public void SetMainMenu(MainMenu mainMenu)
        {
            MainMenu = mainMenu;
        }

        public static bool StartNotification(
            (string tableReference, string tableEntryReference) localizedName,
            (string tableReference, string tableEntryReference) localizedDescription,
            IEnumerable<string> descriptionArgs,
            RandomSfx sfx = null,
            EItem item = EItem.Key
        )
        {
            if (Instance?.NotificationQueueManager == null)
            {
                Log.LogWarning("NotificationQueueManager is not initialized");
                return false;
            }

            Instance.NotificationQueueManager.EnqueueNotification(
                localizedName,
                localizedDescription,
                descriptionArgs,
                sfx,
                item
            );

            return true;
        }

        public static void GoToMainMenu()
        {
            if (GameManager.Instance == null || GameManager.Instance.player == null)
            {
                if (WindowManager.activeWindow is CharacterMenu menu)
                {
                    menu.b_back.button.onClick.Invoke();
                }

                if (WindowManager.activeWindow.name.Contains("Maps And Stats"))
                {
                    WindowManager.activeWindow.allButtons.ToArray().FirstOrDefault(b => b.name == "B_Back")?.button.onClick.Invoke();
                    (WindowManager.activeWindow as CharacterMenu)?.b_back.button.onClick.Invoke();
                }
            }
            else
            {
                TransitionUI.Instance.LoadMenu();
            }
        }

        public void ShowModal(string message)
        {
            if (modal != null)
            {
                modal.UpdateMessage(Loc.T(message));
                return;
            }
            modal = LoadingModal.Show(Loc.T(message));
        }

        public void HideModal()
        {
            if (modal != null)
            {
                modal.Close();
                modal = null;
            }
        }

        public static void ShowUpdateAvailableModal()
        {
            var go = new GameObject("UpdateAvailableModal");
            go.AddComponent<UpdateAvailableModal>();
        }

        public void SavePlayerInventoryActions()
        {
            originalWeaponAddedAction = WeaponInventory.A_WeaponAdded;
            originalStatUpdateAction = PlayerStatsNew.A_StatUpdate;
            WeaponInventory.A_WeaponAdded = null;
            PlayerStatsNew.A_StatUpdate = null;
        }

        public void RestorePlayerInventoryActions()
        {
            if (originalWeaponAddedAction == null)
            {
                Log.LogWarning("WeaponAdded action not saved");
                return;
            }

            if (originalStatUpdateAction == null)
            {
                Log.LogWarning("StatUpdate action not saved");
                return;
            }

            WeaponInventory.A_WeaponAdded = originalWeaponAddedAction;
            PlayerStatsNew.A_StatUpdate = originalStatUpdateAction;
            originalWeaponAddedAction = null;
            originalStatUpdateAction = null;
        }

        public MapEventsDesert GetMapEventsDesert()
        {
            if (mapEventsDesert == null && mapEventsManager == null)
            {
                mapEventsManager = GetMapEventsManager();
                mapEventsDesert = IL2CPP.PointerToValueGeneric<MapEventsDesert>(mapEventsManager.mapEvents.Pointer, false, false);
            }

            return mapEventsDesert;
        }

        private MapEventsManager GetMapEventsManager()
        {
            if (mapEventsManager == null)
            {
                mapEventsManager = Il2CppFindHelper.FindAllGameObjects()
                    .FirstOrDefault(go => go.GetComponent<MapEventsManager>() != null)
                    .GetComponent<MapEventsManager>();
            }
            return mapEventsManager;
        }

        public void ClearMapEventsManager()
        {
            mapEventsManager = null;
            mapEventsDesert = null;
        }

        public void SetWorldSize(Vector3 size)
        {
            WorldSize = size;
        }

        public Vector3 GetWorldSize()
        {
            return WorldSize;
        }

        public void ResetWorldSize()
        {
            WorldSize = Vector3.zero;
            OriginalWorldSize = Vector3.zero;
        }

        /// <summary>
        /// Level change / back to menu: clear the caches keyed by native object pointers (patches)
        /// </summary>
        public static void ResetPointerCaches()
        {
            Patches.Enemies.EnemyPatch.ResetCaches();
            Patches.Projectiles.ProjectileBasePatches.ResetCaches();
            Patches.Unity.UnityComponentPatches.ResetCaches();
            Patches.Unity.UnityObjectPatches.ResetCaches();
        }

        public static DistanceToPlayer GetDistanceToPlayer(Vector3 position)
        {
            var frameIndex = NetworkHandler.FrameIndex; //Time.frameCount is a native call, this is asked per enemy/projectile
            if (frameIndex != distanceTargetFrame)
            {
                var player = GameManager.Instance.player;
                if (player == null)
                {
                    return DistanceToPlayer.Far;
                }

                var target = player.transform.position;
                var spectated = player.IsDead() ? Instance.CameraSwitcher?.GetCurrentTarget() : null;
                if (spectated != null) //Nobody left to spectate (solo, everyone dead): stay on the own body
                {
                    target = spectated.position;
                }

                distanceTarget = target;
                distanceTargetFrame = frameIndex;
            }

            var distance = Vector3.Distance(position, distanceTarget);
            if (distance < 25f)
            {
                return DistanceToPlayer.Close;
            }
            else if (distance < 60f)
            {
                return DistanceToPlayer.Medium;
            }
            else
            {
                return DistanceToPlayer.Far;
            }
        }

    }
}
