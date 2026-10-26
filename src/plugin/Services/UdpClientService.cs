using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Managers;
using BepInEx.Logging;
using LiteNetLib;
using LiteNetLib.Utils;
using Megaphonk.Common.Messages;
using Megaphonk.Common.Messages.GameNetworkMessages;
using Megaphonk.Common.Models;
using Megaphonk.Extensions;
using Megaphonk.Helpers;
using Megaphonk.Steam;
using MemoryPack;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Megaphonk.Services
{
    internal class PeerIntroduction
    {
        public string Name { get; internal set; }
        public uint ConnectionId { get; internal set; }
        public bool IsHost { get; internal set; }
        public bool HasSelected { get; set; }
        public int Latency { get; set; }

        public PeerIntroduction(string name, uint connectionId, bool isHost, bool hasSelected = false)
        {
            Name = name;
            ConnectionId = connectionId;
            IsHost = isHost;
            HasSelected = hasSelected;
            Latency = 0;
        }
    }

    public interface IUdpClientService
    {
        public bool Initialize();
        public void Update();
        /// <summary>
        /// Send the full inventories in the player updates for a moment (peer connected, level changed)
        /// </summary>
        public void ForceFullInventories();

        public void Poll();

        public Task<bool> HandleMatch(MatchInfo matchInfo, uint selfConnectionId, string rdvServerHost, uint rdvServerPort, bool enabledSharedExperience);
        public bool HasAllPeersConnected();

        public void SendToAllClients<T>(T data, DeliveryMethod deliveryMethod) where T : IGameNetworkMessage;
        public void SendToAllClients(byte[] data, DeliveryMethod deliveryMethod);

        public void SendToHost<T>(T data, DeliveryMethod? deliveryMethod = null) where T : IGameNetworkMessage;
        public void SendToClient<T>(NetPeer client, T data, uint netPlayerId) where T : IGameNetworkMessage;
        public void SendToAllClientsExcept<T>(int netPlayerId, uint sender, T data) where T : IGameNetworkMessage;

        /// <summary>
        /// Send the messages queued this frame, packed in MessageBatch packets. Called once per frame (NetworkHandler.LateUpdate).
        /// </summary>
        public void FlushPendingMessages();
        public bool? IsHost();
        public void UpdateEnemies();
        public void UpdateProjectiles();
        public void UpdateTumbleWeeds();

        public void Reset();
        public void GameOver();

        public int GetNetPeerCount();
        public bool AreAllPeersReady();
        public int GetCurrentReadyPeersCount();
        public int GetLatency(uint connectionId);
        public void UpdateMode(bool isHost);
        public bool IsHandlingConnection();
        public void CancelAnyNatIntroduction();
        public bool HasHandledHost();
        public void ResetHandledHost();
        public void RemovePeer(uint clientConnectionId);
        public int GetLocalPort();

        /// <summary>
        /// Friendlies host: the host is in its own room as soon as it opens, before anyone joins (HandleMatch only runs on a join)
        /// </summary>
        public void RegisterHost(uint selfConnectionId, int seed, int resetGeneration);

        /// <summary>
        /// Bumped by each Reset: work started before a reset can tell it's stale
        /// </summary>
        public int GetResetGeneration();

        /// <summary>
        /// Friendlies host alone in its room: no other player and no peer connected or connecting.
        /// The run can then start (and go on) with the host only.
        /// </summary>
        public bool IsSoloSession();
    }
    internal class UdpClientService(
            IPlayerManagerService playerManagerService,
            IEnemyManagerService enemyManagerService,
            IProjectileManagerService projectileManagerService,
            IFinalBossOrbManagerService finalBossOrbManagerService,
            ISpawnedObjectManagerService spawnedObjectManagerService,
            IEncounterService encounterService,
            ISteamTransport steamTransport,
            ManualLogSource logger) : IUdpClientService
    {
        private const int MAX_PACKET_SIZE_BYTES = 1000;
        // Packed enemies per EnemiesUpdate: room left for the message header and a relay envelope
        private const int ENEMY_SNAPSHOT_MAX_BYTES = MAX_PACKET_SIZE_BYTES - RELAY_ENVELOPE_OVERHEAD_BYTES - 16;
        // Inventories are sent only when they changed, and at least this often (player updates are unreliable)
        private const float INVENTORY_RESEND_INTERVAL_SECONDS = 2f;
        // Last inventory sent per player (client: only the local one), main thread only
        private readonly Dictionary<uint, (InventoryInfo Inventory, float SentAt)> sentInventories = [];
        // Until then (Environment.TickCount64, settable from any thread) every update carries the full inventories
        private const long FORCE_INVENTORIES_MS = 1000;
        private long forceInventoriesUntilMs = 0;
        private readonly List<System.Numerics.Vector3> enemyReferencePositions = [];
        // Enemy snapshot acks: host side packet history, client side what arrived (sent ~10 Hz)
        private EnemySnapshotHistory enemySnapshotHistory = new();
        private EnemySnapshotAckBuilder enemyAckBuilder = new();
        private readonly List<int> enemyBlobCounts = [];
        private int ackUpdateCounter = 0;
        private const int ACK_EVERY_UPDATES = 6; // Update() runs at 60 Hz
        // Who can decode compressed packets (PeerCapabilities): direct peers by NetPeer id, everyone by connection id (relay)
        private readonly ConcurrentDictionary<int, byte> compressionPeerIds = [];
        private readonly ConcurrentDictionary<uint, byte> compressionConnectionIds = [];
        private int undecodableCompressedPackets = 0;
        // Host: remote players whose position was received at least once (PlayerUpdate)
        private readonly ConcurrentDictionary<uint, byte> positionReceivedPlayers = [];
        private const int STARTING_GAME_UDP_PORT = 27015;
        private int GAME_UDP_PORT = STARTING_GAME_UDP_PORT;
        private NetManager netManager;
        private EventBasedNetListener listener;
        private EventBasedNatPunchListener natListener;
        private TaskCompletionSource<bool> natPunchComplete;
        private readonly ConcurrentDictionary<int, NetPeer> gamePeers = [];
        private uint? selfConnectionId;
        private readonly ConcurrentDictionary<int, PeerIntroduction> gamePeersIntroduced = [];
        private readonly ConcurrentDictionary<uint, PeerIntroduction> gamePeersIntroducedByRelay = [];
        private int undecodablePackets = 0;
        private bool? isHost { get; set; } = null;
        private int expectedPeerCount = 0;
        private bool hasStarted = false;
        private bool hasAllPeersConnected = false;
        private volatile bool isHandlingConnection = false;
        private bool hasHandledHost = false;
        private volatile int resetGeneration = 0;
        private readonly object registerHostLock = new();
        private IWebsocketClientService matchmakingService;
        private bool isGameOver = false;

        private string rdvServerHost;
        private int rdvServerPort;
        private readonly HashSet<uint> usesRelay = [];
        private NetPeer relayPeer = null;
        private readonly object relayPeerLock = new();

        private ConcurrentDictionary<string, bool> tokens = new();
        private bool hasTriedForceRelay = false;

        private const int POLL_INTERVAL_MS = 5;
        private const int STEAM_CONNECT_TIMEOUT_MS = 15000;
        private const int STEAM_MAX_CONNECT_ATTEMPTS = 30;

        private CancellationTokenSource pollingCancelationTokenSource;

        /// <summary>
        /// Where a relayed message goes: everyone, one client (TargetConnectionId) or everyone but one (ToFilters)
        /// </summary>
        private readonly record struct RelayTarget(uint? TargetConnectionId, uint? ExceptConnectionId);

        // Typed messages are not sent right away: they are queued per destination and delivery method and sent once per frame
        // packed in MessageBatch packets. In late game hundreds of small messages per second (pickups, xp, enemies...) went out
        // one packet each, saturating the 64 packets reliable window, then arrived in bursts.
        // Sends can come from the network threads (polling task while connecting), hence the lock.
        private readonly object pendingLock = new();
        private bool isBatchingActive = false; // Set by the first flush: until NetworkHandler flushes every frame, send right away
        private readonly Dictionary<(NetPeer Peer, DeliveryMethod DeliveryMethod), List<byte[]>> pendingByPeer = [];
        private readonly Dictionary<DeliveryMethod, List<(RelayTarget Target, byte[] Data)>> pendingByRelay = [];
        private const int RELAY_ENVELOPE_OVERHEAD_BYTES = 32;
        private int failedQueuedSends = 0;

        // A MessageBatch is the union tag, its member count and the list length followed by each message serialized on its own,
        // so it is built from the bytes already serialized (callers may reuse or change their message after sending it)
        private static readonly byte[] batchHeader = MemoryPackSerializer.Serialize<IGameNetworkMessage>(new MessageBatch());
        private static readonly bool isBatchFormatValid = CheckBatchFormat();

        // Packets of the per frame flush are compressed (PacketCompression) when that makes them smaller
        private static readonly bool isCompressionAvailable = CheckCompression();
        // The version check is always sent alone and uncompressed: any other build can read it and tell the player
        private static readonly byte modVersionInfoTag = MemoryPackSerializer.Serialize<IGameNetworkMessage>(new ModVersionInfo())[0];

        public bool Initialize()
        {
            if (hasStarted)
            {
                return true;
            }

            listener = new EventBasedNetListener();
            natListener = new EventBasedNatPunchListener();
            netManager = new NetManager(listener)
            {
                IPv6Enabled = true,
                UnconnectedMessagesEnabled = true,
                NatPunchEnabled = true,
                EnableStatistics = true,
                DisconnectTimeout = 15000,
                UpdateTime = POLL_INTERVAL_MS
            };

            bool portInUse = true;
            while (portInUse)
            {
                try
                {
                    hasStarted = netManager.Start(GAME_UDP_PORT);
                    if (hasStarted)
                    {
                        portInUse = false;
                    }
                    else
                    {
                        GAME_UDP_PORT++;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Port {GAME_UDP_PORT} in use, trying next port. Exception: {ex.Message}");
                    GAME_UDP_PORT++;
                }
            }

            if (!hasStarted)
            {
                Plugin.Log.LogError("Failed to start NetManager");
                return false;
            }

            Plugin.Log.LogInfo($"UDPClient listening on port {GAME_UDP_PORT}");

            netManager.NatPunchModule.Init(natListener);

            natListener.NatIntroductionRequest += (local, remote, token) =>
            {
                // ignore on client side
            };

            natListener.NatIntroductionSuccess += (target, natType, token) =>
            {
                try
                {
                    if (!tokens.TryAdd(token, true)) // Atomique!
                    {
                        Plugin.Log.LogWarning($"Duplicate NAT introduction success with token={token}, ignoring.");
                        return;
                    }

                    Plugin.Log.LogInfo($"NAT introduction success, natType={natType}, token={token}");

                    if (netManager != null && netManager.IsRunning)
                    {
                        Plugin.Log.LogInfo($"Connecting...");
                        netManager.Connect(target, "yourKey"); //TODO: technically we should use a key but do we really care ? will check later
                    }
                    else
                    {
                        Plugin.Log.LogError("NetManager is not running, cannot connect");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Error in NAT introduction success handler: {ex}");
                }
            };

            listener.ConnectionRequestEvent += request =>
            {
                Plugin.Log.LogInfo($"Got a connection request from remote"); //TODO: technically we should validate the request key here
                request.Accept();
            };

            listener.PeerConnectedEvent += peer =>
            {
                if (this.rdvServerHost == null || peer.Address.ToString() != DnsHelper.ResolveDomainToIp(this.rdvServerHost))
                {
                    gamePeers.TryAdd(peer.Id, peer);
                    ForceFullInventories();
                }
                else
                {
                    logger.LogInfo($"Connected to relay server: {peer.Id}");
                    lock (relayPeerLock)
                    {
                        relayPeer = peer;
                    }

                    var writer = new NetDataWriter();
                    writer.Put($"{selfConnectionId}|RELAY_BIND");
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);
                }

                if (isHost == null)
                {
                    Plugin.Log.LogError("IsHost not set?!");
                    return;
                }

                if (isHost.HasValue && isHost.Value)
                {
                    Plugin.Log.LogInfo($"Host: Client connected ({gamePeers.Count + usesRelay.Count}/{expectedPeerCount})");
                }
                else
                {
                    Plugin.Log.LogInfo($"Client: Connected to host");
                    IGameNetworkMessage introduced = new Introduced
                    {
                        ConnectionId = selfConnectionId.Value,
                        Name = Configuration.ModConfig.PlayerName.Value,
                        IsHost =
                            Plugin.Instance.Mode.Mode == NetworkModeType.Random && isHost.HasValue && isHost.Value
                            || Plugin.Instance.Mode.Mode == NetworkModeType.Friendlies && Plugin.Instance.Mode.Role == Role.Host
                    };

                    SendToHost(introduced);
                    SendToHost(new PeerCapabilities { OwnerId = selfConnectionId.Value, SupportsCompression = isCompressionAvailable }, DeliveryMethod.ReliableOrdered);
                }
            };

            listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
            {
                try
                {
                    // Deserialized straight from the packet buffer, no copy: MemoryPack copies what it keeps (arrays, strings)
                    // and the reader is only recycled after this handler returns
                    var data = new ReadOnlySpan<byte>(reader.RawData, reader.Position, reader.AvailableBytes);
                    if (PacketCompression.IsCompressed(data))
                    {
                        if (!isCompressionAvailable || !PacketCompression.TryDecompress(data, out var decompressed))
                        {
                            undecodableCompressedPackets++;
                            if (undecodableCompressedPackets == 1)
                            {
                                logger.LogWarning($"Could not decompress a packet from {peer.Address} (compression available here: {isCompressionAvailable}), dropping such packets");
                            }
                            return;
                        }
                        data = decompressed;
                    }

                    IGameNetworkMessage deserializedMsg;
                    try
                    {
                        deserializedMsg = MemoryPackSerializer.Deserialize<IGameNetworkMessage>(data);
                    }
                    catch (MemoryPackSerializationException)
                    {
                        // Usually a player with another build of the mod (different network messages): make it visible
                        undecodablePackets++;
                        if (undecodablePackets <= 3 || undecodablePackets % 200 == 0)
                        {
                            logger.LogWarning($"Undecodable packet from {peer.Address} ({undecodablePackets} so far): does every player have the same mod build?");
                        }
                        return;
                    }

                    if (deserializedMsg == null)
                    {
                        logger.LogDebug($"Failed to deserialize message from {peer.Address}");
                        return;
                    }

                    HandleMessage(deserializedMsg, peer.Id);
                }
                catch (MemoryPackSerializationException)
                {
                    logger.LogDebug($"Packet corruption detected from {peer.Address}, discarding");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Unexpected error handling network : {ex}");
                }
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                logger.LogInfo($"Peer disconnected: {info.Reason}");

                if (peer == null)
                {
                    return;
                }

                HandleDisconnectedPeer(peer);
            };

            listener.NetworkErrorEvent += (endPoint, socketError) =>
            {
                Plugin.Log.LogError($"Network error to {endPoint}: {socketError}");
            };

            listener.NetworkLatencyUpdateEvent += (peer, latency) =>
            {
                var peerIntro = gamePeersIntroduced.FirstOrDefault(p => p.Key == peer.Id);
                if (peerIntro.Value != null)
                {
                    peerIntro.Value.Latency = latency;
                    gamePeersIntroduced[peerIntro.Key] = peerIntro.Value;
                }
            };

            listener.NetworkReceiveUnconnectedEvent += (endPoint, reader, messageType) =>
            {
                var msg = reader.GetString();

                (var mode, var remoteConnectionId, var remoteEndpoint) = msg.Split('|') switch
                {
                    [var m, var rcid, var rep] => (m, uint.Parse(rcid), rep),
                    _ => (null, 0u, null)
                };

                if (mode == null)
                {
                    Plugin.Log.LogWarning($"Invalid unconnected message format: {msg}");
                    return;
                }

                if (mode == "USE_RELAY")
                {
                    logger.LogInfo($"Received USE_RELAY instruction, connecting to relay server...");
                    usesRelay.Add(remoteConnectionId);

                    bool alreadyConnected;
                    lock (relayPeerLock)
                    {
                        alreadyConnected = relayPeer != null;
                    }
                    if (alreadyConnected)
                    {
                        logger.LogInfo($"Already connected to relay server.");
                        return;
                    }

                    var connectionKey = $"{selfConnectionId.Value}|{remoteEndpoint}|RELAY";
                    netManager.Connect(rdvServerHost, rdvServerPort, connectionKey);
                }
            };

            return hasStarted;
        }

        public int GetNetPeerCount()
        {
            return gamePeers.Count;
        }

        private void HandleDisconnectedPeer(NetPeer peer)
        {
            DropPendingMessages(peer);

            if (usesRelay.Any())
            {
                bool isRelayPeer;
                lock (relayPeerLock)
                {
                    isRelayPeer = relayPeer == peer;
                }
                if (isRelayPeer)
                {
                    Plugin.Log.LogInfo($"Relay peer disconnected.");
                    lock (relayPeerLock)
                    {
                        relayPeer = null;
                    }
                    usesRelay.Clear();

                    var host = gamePeersIntroducedByRelay.FirstOrDefault(p => p.Value.IsHost);
                    gamePeersIntroducedByRelay.Clear();

                    Plugin.StartNotification(
                    ("Megaphonk", "ClientDisconnected"),
                    ("Megaphonk", "ClientDisconnected_Description"),
                    [],
                    AudioManager.Instance.uiAbort,
                    item: EItem.BobDead);

                    Plugin.GoToMainMenu();
                    return;
                }

            }


            if (!gamePeers.TryRemove(peer.Id, out _))
            {
                Plugin.Log.LogWarning($"Disconnected peer {peer.Id} not found in gamePeers");
                return;
            }

            if (!gamePeersIntroduced.TryRemove(peer.Id, out var introInfo))
            {
                Plugin.Log.LogWarning($"Disconnected peer {peer.Id} introduction info not found");
                return;
            }


            if (usesRelay.Any())
            {
                if (!isHost.Value && usesRelay.Count == 1)
                {
                    usesRelay.Clear();
                }
                else if (isHost.Value)
                {
                    var info = gamePeersIntroduced.FirstOrDefault(p => p.Value.ConnectionId == peer.Id);
                    if (info.Value != null)
                    {
                        usesRelay.Remove(info.Value.ConnectionId);
                    }
                }
            }


            if (introInfo.IsHost)
            {
                Plugin.StartNotification(
                    ("Megaphonk", "HostDisconnected"),
                    ("Megaphonk", "HostDisconnected_Description"),
                    [introInfo.Name],
                    AudioManager.Instance.uiAbort,
                    item: EItem.BobDead
                );
                Plugin.GoToMainMenu();
            }
            else
            {
                if (gamePeers.IsEmpty && (Plugin.Instance.Mode.Mode == NetworkModeType.Random || Plugin.Instance.Mode.Mode == NetworkModeType.Friendlies && GameManager.Instance?.player != null))
                {
                    Plugin.Log.LogInfo($"All players disconnected, returning to main menu. : is host : {isHost.Value}");

                    Plugin.StartNotification(
                        ("Megaphonk", "AllPlayerDisconnected"),
                        ("Megaphonk", "AllPlayerDisconnected_Description"),
                        [introInfo.Name],
                        AudioManager.Instance.uiAbort,
                        item: EItem.BobDead
                    );
                    Plugin.GoToMainMenu();
                }
                else
                {
                    if (isHost.HasValue && isHost.Value)
                    {
                        IGameNetworkMessage disconnectedPlayer = new PlayerDisconnected
                        {
                            ConnectionId = introInfo.ConnectionId
                        };

                        EventManager.OnPlayerDisconnected(disconnectedPlayer as PlayerDisconnected);
                        SendToAllClients(disconnectedPlayer, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
        }

        public bool AreAllPeersReady()
        {
            if (!isHost.HasValue || !isHost.Value)
            {
                return false;
            }

            var areAllRelayReady = true;
            var areGamePeersReady = true;

            if (usesRelay.Any())
            {
                areAllRelayReady = gamePeersIntroducedByRelay.Values.All(p => p.HasSelected);
            }

            areGamePeersReady = gamePeersIntroduced.Values.All(p => p.HasSelected);

            return areAllRelayReady && areGamePeersReady;
        }

        public int GetCurrentReadyPeersCount()
        {
            if (!isHost.HasValue || !isHost.Value)
            {
                return 0;
            }

            int readyCount = 0;

            if (usesRelay.Any())
            {
                readyCount += gamePeersIntroducedByRelay.Values.Count(p => p.HasSelected);
            }

            readyCount += gamePeersIntroduced.Values.Count(p => p.HasSelected);

            return readyCount;
        }

        private void HandleMessage(IGameNetworkMessage message, int netPeerId)
        {
            if (message is MessageBatch batch)
            {
                HandleMessageBatch(batch, netPeerId);
                return;
            }

            if (!isHost.Value)
            {
                switch (message)
                {
                    case Introduced introduced:
                        ForceFullInventories();
                        if (relayPeer != null && netPeerId == relayPeer.Id)
                        {
                            if (!gamePeersIntroducedByRelay.TryAdd(introduced.ConnectionId, new PeerIntroduction(introduced.Name, introduced.ConnectionId, introduced.IsHost)))
                            {
                                Plugin.Log.LogWarning($"Duplicate introduction from relay for host={netPeerId}, ignoring.");
                            }

                            var playerByRelay = playerManagerService.GetPlayer(introduced.ConnectionId);
                            if (playerByRelay != null)
                            {
                                playerByRelay.Name = introduced.Name;
                                playerManagerService.UpdatePlayer(playerByRelay);
                            }

                            return;
                        }

                        if (!gamePeersIntroduced.TryAdd(netPeerId, new PeerIntroduction(introduced.Name, introduced.ConnectionId, introduced.IsHost)))
                        {
                            Plugin.Log.LogWarning($"Duplicate introduction from host={netPeerId}, ignoring.");
                            return;
                        }

                        var player = playerManagerService.GetPlayer(introduced.ConnectionId);
                        if (player != null)
                        {
                            player.Name = introduced.Name;
                            playerManagerService.UpdatePlayer(player);
                        }

                        break;
                    case PlayerDisconnected playerDisconnected:
                        if (usesRelay.Any())
                        {
                            var disconnectedPeerByRelay = gamePeersIntroducedByRelay.FirstOrDefault(p => p.Value.ConnectionId == playerDisconnected.ConnectionId);
                            if (disconnectedPeerByRelay.Value != null)
                            {
                                if (disconnectedPeerByRelay.Value.IsHost)
                                {
                                    logger.LogWarning($"Host disconnected via relay.");
                                    HandleDisconnectedPeer(relayPeer);
                                }
                                else
                                {
                                    EventManager.OnPlayerDisconnected(playerDisconnected);
                                }

                                return;
                            }
                        }

                        var disconnectedPeer = gamePeersIntroduced.FirstOrDefault(p => p.Value.ConnectionId == playerDisconnected.ConnectionId);

                        if (disconnectedPeer.Value == null) //Disonnected peer not a host
                        {
                            EventManager.OnPlayerDisconnected(playerDisconnected);
                            return;
                        }

                        //Host disconnected
                        var peer = gamePeers.FirstOrDefault(p => p.Value.Id == disconnectedPeer.Key).Value;
                        HandleDisconnectedPeer(peer);

                        break;
                    case LobbyUpdates lobbyUpdate:
                        OnLobbyUpdate(lobbyUpdate);
                        break;
                    case EnemiesUpdate enemiesUpdate:
                        enemyAckBuilder.OnReceived(enemiesUpdate.Seq);
                        EventManager.OnEnemiesUpdate(enemiesUpdate);
                        break;
                    case PeerCapabilities hostCapabilities:
                        OnPeerCapabilities(hostCapabilities, netPeerId);
                        break;
                    case ProjectilesUpdate projectilesUpdate:
                        EventManager.OnProjectilesUpdate(projectilesUpdate.Projectiles);
                        break;
                    case SpawnedObject spawnedObject:
                        EventManager.OnSpawnedObject(spawnedObject);
                        break;
                    case SpawnedEnemy spawnedEnemy:
                        EventManager.OnSpawnedEnemy(spawnedEnemy);
                        break;
                    case AbstractSpawnedProjectile spawnedProjectile:
                        EventManager.OnSpawnedProjectile(spawnedProjectile);
                        break;
                    case SelectedCharacter selectedCharacter:
                        EventManager.OnSelectedCharacter(selectedCharacter);
                        break;
                    case EnemyDied enemyDied:
                        EventManager.OnEnemyDied(enemyDied);
                        break;
                    case ProjectileDone projectileDone:
                        EventManager.OnProjectileDone(projectileDone);
                        break;
                    case SpawnedPickupOrb spawnedPickup:
                        EventManager.OnSpawnedPickupOrb(spawnedPickup);
                        break;
                    case SpawnedPickup spawnedPickupItem:
                        EventManager.OnSpawnedPickup(spawnedPickupItem);
                        break;
                    case PickupFollowingPlayer pickupFollowingPlayer:
                        EventManager.OnPickupFollowingPlayer(pickupFollowingPlayer);
                        break;
                    case PickupApplied pickupApplied:
                        EventManager.OnPickupApplied(pickupApplied);
                        break;
                    case SpawnedChest spawnedChest:
                        EventManager.OnSpawnedChest(spawnedChest);
                        break;
                    case ChestOpened chestOpened:
                        EventManager.OnChestOpened(chestOpened);
                        break;
                    case WeaponAdded weaponAdded:
                        EventManager.OnWeaponAdded(weaponAdded);
                        break;
                    case InteractableUsed interactableUsed:
                        EventManager.OnInteractableUsed(interactableUsed);
                        break;
                    case StartingChargingShrine startingChargingShrine:
                        EventManager.OnStartingChargingShrine(startingChargingShrine);
                        break;
                    case StoppingChargingShrine stoppingChargingShrine:
                        EventManager.OnStoppingChargingShrine(stoppingChargingShrine);
                        break;
                    case EnemyExploder enemyExploder:
                        EventManager.OnEnemyExploder(enemyExploder);
                        break;
                    case EnemyDamaged enemyDamaged:
                        EventManager.OnEnemyDamaged(enemyDamaged);
                        break;
                    case SpawnedEnemySpecialAttack spawnedEnemySpecialAttack:
                        EventManager.OnSpawnedEnemySpecialAttack(spawnedEnemySpecialAttack);
                        break;
                    case StartingChargingPylon startingChargingPylon:
                        EventManager.OnStartingChargingPylon(startingChargingPylon);
                        break;
                    case StoppingChargingPylon stoppingChargingPylon:
                        EventManager.OnStoppingChargingPylon(stoppingChargingPylon);
                        break;
                    case FinalBossOrbSpawned finalBossOrbSpawned:
                        EventManager.OnFinalBossOrbSpawned(finalBossOrbSpawned);
                        break;
                    case FinalBossOrbDestroyed finalBossOrbDestroyed:
                        EventManager.OnFinalBossOrbDestroyed(finalBossOrbDestroyed);
                        break;
                    case StartedSwarmEvent startedSwarmEvent:
                        EventManager.OnStartedSwarmEvent(startedSwarmEvent);
                        break;
                    case GameOver gameOver:
                        EventManager.OnGameOver(gameOver);
                        break;
                    case RetargetedEnemies retargetedEnemies:
                        EventManager.OnRetargetedEnemies(retargetedEnemies);
                        break;
                    case RunStarted runStarted:
                        EventManager.OnRunStarted(runStarted);
                        break;
                    case TomeAdded tomeAdded:
                        EventManager.OnTomeAdded(tomeAdded);
                        break;
                    case LightningStrike lightningStrike:
                        EventManager.OnLightningStrike(lightningStrike);
                        break;
                    case TornadoesSpawned tornadoesSpawned:
                        EventManager.OnTornadoesSpawned(tornadoesSpawned);
                        break;
                    case StormStarted stormStarted:
                        EventManager.OnStormStarted(stormStarted);
                        break;
                    case StormStopped stormStopped:
                        EventManager.OnStormStopped(stormStopped);
                        break;
                    case TumbleWeedSpawned tumbleWeedSpawned:
                        EventManager.OnTumbleWeedSpawned(tumbleWeedSpawned);
                        break;
                    case TumbleWeedsUpdate tumbleWeedsUpdate:
                        EventManager.OnTumbleWeedsUpdate(tumbleWeedsUpdate.TumbleWeeds);
                        break;
                    case TumbleWeedDespawned tumbleWeedDespawned:
                        EventManager.OnTumbleWeedDespawned(tumbleWeedDespawned);
                        break;
                    case ItemAdded itemAdded:
                        EventManager.OnItemAdded(itemAdded);
                        break;
                    case ItemRemoved itemRemoved:
                        EventManager.OnItemRemoved(itemRemoved);
                        break;
                    case WeaponToggled weaponToggled:
                        EventManager.OnWeaponToggled(weaponToggled);
                        break;
                    case SpawnedObjectInCrypt spawnedObjectInCrypt:
                        EventManager.OnSpawnedObjectInCrypt(spawnedObjectInCrypt);
                        break;
                    case StartingChargingLamp startingChargingLamp:
                        EventManager.OnStartingChargingLamp(startingChargingLamp);
                        break;
                    case StoppingChargingLamp stoppingChargingLamp:
                        EventManager.OnStoppingChargingLamp(stoppingChargingLamp);
                        break;
                    case TimerStarted timerStarted:
                        EventManager.OnTimerStarted(timerStarted);
                        break;
                    case HatChanged hatChanged:
                        EventManager.OnHatChanged(hatChanged);
                        break;
                    case SpawnedReviver spawnedReviver:
                        EventManager.OnSpawnedReviver(spawnedReviver);
                        break;
                    case PlayerRespawned playerRespawned:
                        EventManager.OnPlayerRespawned(playerRespawned);
                        break;
                    case PlayerDied playerDied:
                        EventManager.OnPlayerDied(playerDied);
                        break;
                    case CloseEncounter closeEncounter:
                        EventManager.OnCloseEncounter(closeEncounter);
                        break;
                    case EncounterPaused encounterPaused:
                        EventManager.OnEncounterPaused(encounterPaused);
                        break;
                    case PlayerStatsChanged playerStatsChanged:
                        EventManager.OnPlayerStatsChanged(playerStatsChanged);
                        break;
                    case ModVersionInfo modVersionInfo:
                        EventManager.OnModVersionInfo(modVersionInfo);
                        break;
                    case PartySettings partySettings:
                        EventManager.OnPartySettings(partySettings);
                        break;
                    case PlayerDownedChanged playerDownedChanged:
                        EventManager.OnPlayerDownedChanged(playerDownedChanged);
                        break;
                    case GiveItem giveItem:
                        EventManager.OnGiveItem(giveItem);
                        break;
                    case PingPlaced pingPlaced:
                        EventManager.OnPingPlaced(pingPlaced);
                        break;
                    case TeamMeterUpdate teamMeterUpdate:
                        EventManager.OnTeamMeterUpdate(teamMeterUpdate);
                        break;
                    case ChallengeNotice challengeNotice:
                        EventManager.OnChallengeNotice(challengeNotice);
                        break;
                    case TeamStats teamStats:
                        EventManager.OnTeamStats(teamStats);
                        break;
                    case MusicTrackChanged musicTrackChanged:
                        EventManager.OnMusicTrackChanged(musicTrackChanged);
                        break;
                    case AscensionStarted ascensionStarted:
                        EventManager.OnAscensionStarted(ascensionStarted);
                        break;
                    case RiftStarted riftStarted:
                        EventManager.OnRiftStarted(riftStarted);
                        break;
                    case RiftProgress riftProgress:
                        EventManager.OnRiftProgress(riftProgress);
                        break;
                    case RiftEnded riftEnded:
                        EventManager.OnRiftEnded(riftEnded);
                        break;
                    case PerkFx perkFx:
                        EventManager.OnPerkFx(perkFx);
                        break;
                    default:
                        Plugin.Log.LogWarning($"Unknown message type received. message={message}");
                        break;
                }
            }
            else
            {
                switch (message)
                {
                    case Introduced introduced:
                        ForceFullInventories();
                        if (relayPeer != null && netPeerId == relayPeer.Id)
                        {
                            if (!gamePeersIntroducedByRelay.TryAdd(introduced.ConnectionId, new PeerIntroduction(introduced.Name, introduced.ConnectionId, introduced.IsHost)))
                            {
                                Plugin.Log.LogWarning($"Duplicate introduction from netPlayerId={netPeerId} via relay, ignoring.");
                            }

                            if (Plugin.Instance.Mode.Mode == Common.Models.NetworkModeType.Friendlies)
                            {
                                Plugin.StartNotification(("Megaphonk", "FriendliesClientJoinSuccess"), ("Megaphonk", "FriendliesClientJoinSuccessDesc"), [introduced.Name]);
                            }

                            return;
                        }
                        else
                        {
                            if (!gamePeersIntroduced.TryAdd(netPeerId, new PeerIntroduction(introduced.Name, introduced.ConnectionId, introduced.IsHost)))
                            {
                                Plugin.Log.LogWarning($"Duplicate introduction from netPlayerId={netPeerId}, ignoring.");
                                return;
                            }
                        }

                        if (Plugin.Instance.Mode.Mode == Common.Models.NetworkModeType.Friendlies)
                        {
                            Plugin.StartNotification(("Megaphonk", "FriendliesClientJoinSuccess"), ("Megaphonk", "FriendliesClientJoinSuccessDesc"), [introduced.Name]);
                        }

                        IGameNetworkMessage introducedResponse = new Introduced
                        {
                            ConnectionId = selfConnectionId.Value,
                            Name = Configuration.ModConfig.PlayerName.Value,
                            IsHost = isHost.Value
                        };

                        var peer = gamePeers.FirstOrDefault(p => p.Value.Id == netPeerId).Value;
                        if (peer != null)
                        {
                            SendToClient(peer, introducedResponse, introduced.ConnectionId);

                            var playerModel = playerManagerService.GetPlayer(introduced.ConnectionId);
                            if (playerModel != null)
                            {
                                playerModel.Name = introduced.Name;
                                playerManagerService.UpdatePlayer(playerModel);
                            }
                        }

                        break;
                    case ClientInGameReady clientInGameReady:
                        var clientReadyId = clientInGameReady.ConnectionId;
                        var player = playerManagerService.GetPlayer(clientReadyId);
                        if (player == null)
                        {
                            Plugin.Log.LogWarning($"Received ClientReady from unknown player with connection ID {clientReadyId}.");
                            return;
                        }
                        player.IsReady = true;
                        playerManagerService.UpdatePlayer(player);

                        Plugin.Log.LogInfo($"Player {clientReadyId} is ready.");

                        break;
                    case EnemiesAck enemiesAck:
                        var lostEnemies = new List<uint>();
                        enemySnapshotHistory.OnAck(enemiesAck.ConnectionId, enemiesAck.Latest, enemiesAck.ReceivedMask, lostEnemies);
                        if (lostEnemies.Count > 0)
                        {
                            enemyManagerService.MarkSendsLost(lostEnemies);
                        }
                        break;
                    case PeerCapabilities clientCapabilities:
                        OnPeerCapabilities(clientCapabilities, netPeerId);
                        // Answer with the host's own, through the same route
                        SendToAllClients(new PeerCapabilities { OwnerId = selfConnectionId ?? 0, SupportsCompression = isCompressionAvailable }, DeliveryMethod.ReliableOrdered);
                        break;
                    case PlayerUpdate playerUpdate:
                        var playerUpdateId = playerUpdate.ConnectionId;
                        var playerToUpdate = playerManagerService.GetPlayer(playerUpdateId);
                        if (playerToUpdate == null)
                        {
                            Plugin.Log.LogWarning($"Received PlayerUpdate from unknown player with connection ID {playerUpdateId}.");
                            return;
                        }

                        playerToUpdate.Position = Quantizer.Quantize(playerUpdate.Position.ToUnityVector3());
                        positionReceivedPlayers[playerUpdateId] = 0;
                        playerToUpdate.MovementState = playerUpdate.MovementState;
                        playerToUpdate.AnimatorState = playerUpdate.AnimatorState;
                        playerToUpdate.ConnectionId = playerUpdate.ConnectionId;
                        //if (playerToUpdate.Hp != 0)
                        //{
                        playerToUpdate.Hp = playerUpdate.Hp;
                        playerToUpdate.Shield = playerUpdate.Shield;
                        //}
                        playerToUpdate.MaxHp = playerUpdate.MaxHp;
                        playerToUpdate.MaxShield = playerUpdate.MaxShield;
                        //playerToUpdate.Xp = playerUpdate.Xp;
                        if (playerUpdate.Inventory != null) //Only sent when it changed
                        {
                            playerToUpdate.Inventory = playerUpdate.Inventory;
                        }
                        playerToUpdate.Name = playerUpdate.Name;

                        playerManagerService.UpdatePlayer(playerToUpdate);

                        EventManager.OnPlayerUpdate(playerUpdate);
                        break;
                    case SelectedCharacter selectedCharacter:

                        if (gamePeersIntroducedByRelay.TryGetValue(selectedCharacter.ConnectionId, out var introInfoByRelay))
                        {
                            introInfoByRelay.HasSelected = true;
                            gamePeersIntroducedByRelay[selectedCharacter.ConnectionId] = introInfoByRelay;
                        }

                        if (gamePeersIntroduced.TryGetValue(netPeerId, out var introInfo))
                        {
                            introInfo.HasSelected = true;
                            gamePeersIntroduced[netPeerId] = introInfo;
                        }

                        var toUpdate = playerManagerService.GetPlayer(selectedCharacter.ConnectionId); //We could technically use EventManager.OnSelectedCharacter but the metrics RunStatistics sent later will miss the update
                        if (toUpdate == null)
                        {
                            logger.LogWarning($"Player not found for ConnectionId: {selectedCharacter.ConnectionId}");
                            return;
                        }

                        toUpdate.Character = selectedCharacter.Character;
                        toUpdate.Skin = selectedCharacter.Skin;
                        playerManagerService.UpdatePlayer(toUpdate);

                        SendToAllClientsExcept(netPeerId, selectedCharacter.ConnectionId, selectedCharacter);

                        if (AreAllPeersReady() && playerManagerService.HasSelectedCharacter() && Plugin.Instance.IS_HOST_READY)
                        {
                            var runConfig = WindowManager.activeWindow.GetComponentInChildren<MapSelectionUi>().runConfig;
                            MapController.StartNewMap(runConfig);
                        }
                        break;
                    case AbstractSpawnedProjectile spawnedProjectile:
                        EventManager.OnSpawnedProjectile(spawnedProjectile);
                        SendToAllClientsExcept(netPeerId, spawnedProjectile.OwnerId, spawnedProjectile);
                        break;
                    case ProjectileDone projectileDone:
                        EventManager.OnProjectileDone(projectileDone);
                        SendToAllClientsExcept(netPeerId, projectileDone.SenderConnectionId, projectileDone);
                        break;
                    case EnemyDied enemyDied:
                        EventManager.OnEnemyDied(enemyDied);
                        SendToAllClientsExcept(netPeerId, enemyDied.DiedByOwnerId, enemyDied);
                        break;
                    case PickupApplied pickupApplied:
                        EventManager.OnPickupApplied(pickupApplied);
                        SendToAllClientsExcept(netPeerId, pickupApplied.OwnerId, pickupApplied);
                        break;
                    case PickupFollowingPlayer pickupFollowingPlayer:
                        EventManager.OnPickupFollowingPlayer(pickupFollowingPlayer);
                        SendToAllClientsExcept(netPeerId, pickupFollowingPlayer.PlayerId, pickupFollowingPlayer);
                        break;
                    case ChestOpened chestOpened:
                        EventManager.OnChestOpened(chestOpened);
                        SendToAllClientsExcept(netPeerId, chestOpened.OwnerId, chestOpened);
                        break;
                    case WeaponAdded weaponAdded:
                        EventManager.OnWeaponAdded(weaponAdded);
                        SendToAllClientsExcept(netPeerId, weaponAdded.OwnerId, weaponAdded);
                        break;
                    case InteractableUsed interactableUsed:
                        EventManager.OnInteractableUsed(interactableUsed);
                        SendToAllClientsExcept(netPeerId, interactableUsed.OwnerId, interactableUsed);
                        break;
                    case StartingChargingShrine startingChargingShrine:
                        EventManager.OnStartingChargingShrine(startingChargingShrine);
                        break;
                    case StoppingChargingShrine stoppingChargingShrine:
                        EventManager.OnStoppingChargingShrine(stoppingChargingShrine);
                        break;
                    case EnemyExploder enemyExploder:
                        EventManager.OnEnemyExploder(enemyExploder);
                        SendToAllClientsExcept(netPeerId, enemyExploder.SenderId, enemyExploder);
                        break;
                    case EnemyDamaged enemyDamaged: //Applying it re-broadcasts it to every client (Enemy.Damage postfix), no relay needed
                        EventManager.OnEnemyDamaged(enemyDamaged);
                        break;
                    //case SpawnedEnemySpecialAttack spawnedEnemySpecialAttack:
                    //    EventManager.OnSpawnedEnemySpecialAttack(spawnedEnemySpecialAttack);
                    //    SendToAllClientsExcept(netPlayerId, spawnedEnemySpecialAttack);
                    //    break;
                    case StartingChargingPylon startingChargingPylon:
                        EventManager.OnStartingChargingPylon(startingChargingPylon);
                        SendToAllClientsExcept(netPeerId, startingChargingPylon.PlayerChargingId, startingChargingPylon);
                        break;
                    case StoppingChargingPylon stoppingChargingPylon:
                        EventManager.OnStoppingChargingPylon(stoppingChargingPylon);
                        SendToAllClientsExcept(netPeerId, stoppingChargingPylon.PlayerChargingId, stoppingChargingPylon);
                        break;
                    //case FinalBossOrbSpawned finalBossOrbSpawned:
                    //    EventManager.OnFinalBossOrbSpawned(finalBossOrbSpawned);
                    //    SendToAllClientsExcept(netPlayerId, finalBossOrbSpawned);
                    //    break;
                    case FinalBossOrbDestroyed finalBossOrbDestroyed:
                        EventManager.OnFinalBossOrbDestroyed(finalBossOrbDestroyed);
                        SendToAllClientsExcept(netPeerId, finalBossOrbDestroyed.SenderId, finalBossOrbDestroyed);
                        break;
                    case PlayerDied playerDied:
                        EventManager.OnPlayerDied(playerDied);
                        break;
                    case TomeAdded tomeAdded:
                        EventManager.OnTomeAdded(tomeAdded);
                        SendToAllClientsExcept(netPeerId, tomeAdded.OwnerId, tomeAdded);
                        break;
                    case InteractableCharacterFightEnemySpawned interactableCharacterFightEnemySpawned:
                        EventManager.OnInteractableCharacterFightEnemySpawned(interactableCharacterFightEnemySpawned);
                        break;
                    case WantToStartFollowingPickup wantToStartFollowingPickup:
                        EventManager.OnWantToStartFollowingPickup(wantToStartFollowingPickup);
                        break;
                    case ItemAdded itemAdded:
                        EventManager.OnItemAdded(itemAdded);
                        SendToAllClientsExcept(netPeerId, itemAdded.OwnerId, itemAdded);
                        break;
                    case ItemRemoved itemRemoved:
                        EventManager.OnItemRemoved(itemRemoved);
                        SendToAllClientsExcept(netPeerId, itemRemoved.OwnerId, itemRemoved);
                        break;
                    case WeaponToggled weaponToggled:
                        EventManager.OnWeaponToggled(weaponToggled);
                        SendToAllClientsExcept(netPeerId, weaponToggled.OwnerId, weaponToggled);
                        break;
                    //case SpawnedObjectInCrypt spawnedObjectInCrypt:
                    //    EventManager.OnSpawnedObjectInCrypt(spawnedObjectInCrypt);
                    //    SendToAllClientsExcept(netPlayerId, spawnedObjectInCrypt);
                    //    break;
                    case StartingChargingLamp startingChargingLamp:
                        EventManager.OnStartingChargingLamp(startingChargingLamp);
                        SendToAllClientsExcept(netPeerId, startingChargingLamp.PlayerChargingId, startingChargingLamp);
                        break;
                    case StoppingChargingLamp stoppingChargingLamp:
                        EventManager.OnStoppingChargingLamp(stoppingChargingLamp);
                        SendToAllClientsExcept(netPeerId, stoppingChargingLamp.PlayerChargingId, stoppingChargingLamp);
                        break;
                    case TimerStarted timerStarted:
                        EventManager.OnTimerStarted(timerStarted);
                        SendToAllClientsExcept(netPeerId, timerStarted.SenderId, timerStarted);
                        break;
                    case HatChanged hatChanged:
                        EventManager.OnHatChanged(hatChanged);
                        SendToAllClientsExcept(netPeerId, hatChanged.OwnerId, hatChanged);
                        break;
                    case PlayerDisconnected playerDisconnected: //Host only receives this message by the rdv server, normally its handled in LiteNet's PeerDisconnectedEvent
                        if (!usesRelay.Remove(playerDisconnected.ConnectionId))
                        {
                            logger.LogInfo($"PlayerDisconnected: ConnectionId {playerDisconnected.ConnectionId} was not using relay.");
                            return;
                        }

                        EventManager.OnPlayerDisconnected(playerDisconnected);
                        SendToAllClients(playerDisconnected, DeliveryMethod.ReliableOrdered);

                        break;
                    case PlayerStatsChanged playerStatsChanged:
                        EventManager.OnPlayerStatsChanged(playerStatsChanged);
                        SendToAllClientsExcept(netPeerId, playerStatsChanged.OwnerId, playerStatsChanged);
                        break;
                    case AscensionStarted ascensionStarted: //A client asks for the next ascension
                        EventManager.OnAscensionStarted(ascensionStarted);
                        break;
                    case PlayerPerks playerPerks: //Only the host runs hit perks: not forwarded
                        EventManager.OnPlayerPerks(playerPerks);
                        break;
                    case ModVersionInfo modVersionInfo:
                        EventManager.OnModVersionInfo(modVersionInfo);
                        SendToAllClientsExcept(netPeerId, modVersionInfo.OwnerId, modVersionInfo);
                        break;
                    case PlayerDownedChanged playerDownedChanged:
                        EventManager.OnPlayerDownedChanged(playerDownedChanged);
                        SendToAllClientsExcept(netPeerId, playerDownedChanged.PlayerId, playerDownedChanged);
                        break;
                    case GiveItem giveItem:
                        EventManager.OnGiveItem(giveItem);
                        SendToAllClientsExcept(netPeerId, giveItem.FromId, giveItem);
                        break;
                    case PingPlaced pingPlaced:
                        EventManager.OnPingPlaced(pingPlaced);
                        SendToAllClientsExcept(netPeerId, pingPlaced.OwnerId, pingPlaced);
                        break;
                    case RunStatsReport runStatsReport:
                        EventManager.OnRunStatsReport(runStatsReport);
                        break;
                    case EncounterStateChanged encounterStateChanged: //Waiting players are released by SynchronizationService.UpdateEncounterSync
                        encounterService.SetRemoteState(encounterStateChanged.OwnerId, encounterStateChanged.State, encounterStateChanged.CloseSeq);

                        if (encounterStateChanged.ForceClose)
                        {
                            encounterService.RequestForceClose();
                        }

                        break;
                    default:
                        Plugin.Log.LogWarning($"Unknown message type received {message}");
                        break;
                }
            }
        }

        /// <summary>
        /// Each message of a batch goes through the same path as if it had arrived on its own (host relaying included)
        /// </summary>
        private void HandleMessageBatch(MessageBatch batch, int netPeerId)
        {
            if (batch.Messages == null)
            {
                return;
            }

            foreach (var message in batch.Messages)
            {
                if (message == null || message is MessageBatch)
                {
                    logger.LogDebug("Ignoring empty or nested message in a batch");
                    continue;
                }

                try
                {
                    HandleMessage(message, netPeerId);
                }
                catch (Exception ex) // One failing message must not drop the rest of the batch
                {
                    Plugin.Log.LogWarning($"Unexpected error handling network : {ex}");
                }
            }
        }

        public void Reset()
        {
            lock (registerHostLock) //A RegisterHost in progress finishes before the identity is cleared below, later ones are dropped
            {
                resetGeneration++;
            }

            lock (pendingLock)
            {
                try
                {
                    FlushPendingMessagesLocked(); // Best effort, the connections are closed right after
                }
                catch (Exception ex)
                {
                    logger.LogDebug($"Failed to flush pending messages on reset: {ex.Message}");
                }

                pendingByPeer.Clear();
                pendingByRelay.Clear();
                isBatchingActive = false;
            }

            hasHandledHost = false;
            selfConnectionId = null;
            isHost = null;
            expectedPeerCount = 0;
            hasAllPeersConnected = false;
            isGameOver = false;
            sentInventories.Clear();
            positionReceivedPlayers.Clear();
            enemySnapshotHistory = new();
            enemyAckBuilder = new();
            compressionPeerIds.Clear();
            compressionConnectionIds.Clear();
            undecodableCompressedPackets = 0;
            gamePeers.Clear();
            netManager?.Stop();
            hasStarted = false;
            gamePeersIntroduced.Clear();
            gamePeersIntroducedByRelay.Clear();
            hasTriedForceRelay = false;
            pollingCancelationTokenSource?.Cancel();
            pollingCancelationTokenSource = null;

            lock (relayPeerLock)
            {
                relayPeer?.Disconnect();
                relayPeer = null;
            }
            usesRelay.Clear();

            netManager?.DisconnectAll();
        }

        public void GameOver()
        {
            isGameOver = true;
        }

        private void OnLobbyUpdate(LobbyUpdates lobbyUpdate) //TODO: move to synchronizationService
        {

            foreach (var player in lobbyUpdate.Players)
            {
                var existingPlayer = playerManagerService.GetPlayer(player.ConnectionId);
                if (existingPlayer != null)
                {
                    //var previousHp = existingPlayer.Hp;
                    //if (previousHp == 0)
                    //{
                    //    player.Hp = 0;
                    //}
                    player.Inventory ??= existingPlayer.Inventory; //Only sent when it changed
                    playerManagerService.UpdatePlayer(player);

                    var playerUpdate = new PlayerUpdate
                    {
                        Position = Quantizer.Dequantize(player.Position).ToNumericsVector3(),
                        MovementState = player.MovementState,
                        AnimatorState = player.AnimatorState,
                        ConnectionId = player.ConnectionId,
                        Hp = player.Hp,
                        MaxHp = player.MaxHp,
                        Shield = player.Shield,
                        MaxShield = player.MaxShield,
                        //Xp = player.Xp,
                        Name = player.Name,
                        Inventory = player.Inventory,
                    };

                    //if (previousHp == 0)
                    //{
                    //    playerUpdate.Hp = 0;
                    //}

                    EventManager.OnPlayerUpdate(playerUpdate);
                }
            }

            EventManager.OnFinalBossOrbsUpdate(lobbyUpdate.BossOrbs);
        }

        public async Task<bool> HandleMatch(MatchInfo matchInfo, uint selfConnectionId, string rdvServerHost, uint rdvServerPort, bool enabledSharedExperience)
        {
            if (hasHandledHost)
            {
                Plugin.Log.LogWarning("Already handled first connection,skipping");
                return false;
            }

            if (!this.selfConnectionId.HasValue) this.selfConnectionId = selfConnectionId;
            if (!this.isHost.HasValue) this.isHost = matchInfo.Peers.FirstOrDefault(p => p.ConnectionId == selfConnectionId)?.IsHost ?? false;

            if (!this.isHost.Value)
            {
                hasHandledHost = true; //Prevent further host connection
            }

            if (!Plugin.Instance.Mode.EnabledSharedExperience.HasValue)
            {
                Plugin.Instance.Mode.EnabledSharedExperience = enabledSharedExperience;
            }

            var allPlayers = playerManagerService.GetAllPlayers();
            foreach (var peer in matchInfo.Peers)
            {
                if (!allPlayers.Any(p => p.ConnectionId == peer.ConnectionId))
                {
                    playerManagerService.AddPlayer(peer.ConnectionId, peer.IsHost.Value, peer.ConnectionId == selfConnectionId);
                }
            }

            playerManagerService.SetSeed((int)matchInfo.Seed);

            Plugin.Log.LogInfo($"I am {(isHost.Value ? "HOST" : "CLIENT")}");

            if (isHost.Value)
            {
                expectedPeerCount = matchInfo.Peers.Count() - 1; // All clients except myself
                Plugin.Log.LogInfo($"Host expecting {expectedPeerCount} client connections");
            }
            else
            {
                expectedPeerCount = 1; // Only the host
                Plugin.Log.LogInfo("Client expecting connection to host");
            }

            var role = isHost.Value ? "host" : "client";
            var hostId = matchInfo.Peers.First(p => p.IsHost == true).ConnectionId.ToString();

            var uniqueToken = $"{role}|{hostId}|{selfConnectionId}";

            this.rdvServerHost = rdvServerHost;
            this.rdvServerPort = (int)rdvServerPort;

            if (steamTransport.IsActive)
            {
                // Steam carries the packets: the client connects to the local port standing for the host, the host waits
                if (!isHost.Value)
                {
                    if (steamTransport.HostProxyPort == null)
                    {
                        Plugin.Log.LogError("Steam transport has no host port");
                        return false;
                    }

                    netManager.MaxConnectAttempts = STEAM_MAX_CONNECT_ATTEMPTS;
                    netManager.Connect("127.0.0.1", steamTransport.HostProxyPort.Value, "yourKey");
                }

                Plugin.Log.LogInfo("Waiting for P2P connections through Steam...");
            }
            else
            {
                Plugin.Log.LogInfo($"Sending NAT punch request to rendezvous server with token {uniqueToken}");

                netManager.NatPunchModule.SendNatIntroduceRequest(rdvServerHost, (int)rdvServerPort, uniqueToken);

                Plugin.Log.LogInfo("Waiting for NAT introductions and P2P connections...");
            }

            natPunchComplete = new TaskCompletionSource<bool>();

            pollingCancelationTokenSource = new CancellationTokenSource();
            var token = pollingCancelationTokenSource.Token;

            var initialPollCts = new CancellationTokenSource();

            var pollTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !initialPollCts.IsCancellationRequested)
                {
                    bool relayConnected;
                    lock (relayPeerLock)
                    {
                        relayConnected = relayPeer != null;
                    }
                    if (expectedPeerCount > 0 && gamePeers.Count + usesRelay.Count >= expectedPeerCount && (!usesRelay.Any() || relayConnected)) break;
                    Poll();
                    await Task.Delay(POLL_INTERVAL_MS);
                }

                if (token.IsCancellationRequested || initialPollCts.IsCancellationRequested)
                {
                    hasAllPeersConnected = false;
                    natPunchComplete.TrySetResult(false);
                    return;
                }

                hasAllPeersConnected = expectedPeerCount > 0 && gamePeers.Count + usesRelay.Count >= expectedPeerCount && (!usesRelay.Any() || relayPeer != null);
                natPunchComplete.TrySetResult(hasAllPeersConnected);
            });

            isHandlingConnection = true;

            var timeoutTask = Task.Delay(steamTransport.IsActive ? STEAM_CONNECT_TIMEOUT_MS : 10000);
            var completedTask = await Task.WhenAny(natPunchComplete.Task, timeoutTask);

            if (token.IsCancellationRequested)
            {
                logger.LogWarning("P2P connection handling was cancelled.");
                isHandlingConnection = false;
                return false;
            }

            if (completedTask == natPunchComplete.Task && await natPunchComplete.Task)
            {
                logger.LogInfo($"P2P connections successful! Connected to {gamePeers.Count + usesRelay.Count} peers");
                isHandlingConnection = false;
                return true;
            }
            else
            {
                initialPollCts.Cancel();

                if (!hasTriedForceRelay && expectedPeerCount > 0 && !steamTransport.IsActive)
                {
                    if (token.IsCancellationRequested)
                    {
                        logger.LogWarning("P2P connection handling was cancelled, no relay attempt");
                        return false;
                    }

                    logger.LogWarning($"P2P connection timeout - only {gamePeers.Count + usesRelay.Count}/{expectedPeerCount} peers connected, retrying with forced relay mode...");
                    hasTriedForceRelay = true;

                    initialPollCts.Cancel();

                    var forceRelayToken = $"{role}|{hostId}|{selfConnectionId}|force_relay";
                    logger.LogInfo($"Sending NAT punch request with force relay: {forceRelayToken}");
                    netManager.NatPunchModule.SendNatIntroduceRequest(rdvServerHost, (int)rdvServerPort, forceRelayToken);

                    natPunchComplete = new TaskCompletionSource<bool>();

                    var retryPollTask = Task.Run(async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            bool relayConnected;
                            lock (relayPeerLock)
                            {
                                relayConnected = relayPeer != null;
                            }
                            if (expectedPeerCount > 0 && gamePeers.Count + usesRelay.Count >= expectedPeerCount && (!usesRelay.Any() || relayConnected))
                                break;
                            Poll();
                            await Task.Delay(POLL_INTERVAL_MS);
                        }

                        if (token.IsCancellationRequested)
                        {
                            hasAllPeersConnected = false;
                            natPunchComplete.TrySetResult(false);
                            return;
                        }

                        hasAllPeersConnected = expectedPeerCount > 0 && gamePeers.Count + usesRelay.Count >= expectedPeerCount && (!usesRelay.Any() || relayPeer != null);
                        natPunchComplete.TrySetResult(hasAllPeersConnected);
                    });

                    var retryTimeoutTask = Task.Delay(10000);
                    var retryCompletedTask = await Task.WhenAny(natPunchComplete.Task, retryTimeoutTask);

                    if (token.IsCancellationRequested)
                    {
                        logger.LogWarning("P2P connection handling was cancelled during force relay retry.");
                        isHandlingConnection = false;
                        return false;
                    }

                    if (retryCompletedTask == natPunchComplete.Task && await natPunchComplete.Task)
                    {
                        logger.LogInfo($"P2P connections successful with forced relay! Connected to {gamePeers.Count + usesRelay.Count} peers");
                        isHandlingConnection = false;
                        return true;
                    }
                }

                logger.LogError($"P2P connection timeout - only {gamePeers.Count + usesRelay.Count}/{expectedPeerCount} peers connected");
                isHandlingConnection = false;
                return gamePeers.Count + usesRelay.Count > 0;
            }
        }

        public void Poll()
        {
            if (hasStarted)
            {
                netManager?.PollEvents();
                netManager?.NatPunchModule.PollEvents();
            }
        }

        public void Update()
        {
            // Built once per tick: it reads the whole local player (position, animator, inventory) through native calls
            var localPlayerUpdate = playerManagerService.GetLocalPlayerUpdate();
            UpdateLocalPlayer(localPlayerUpdate);

            if (isGameOver)
            {
                return;
            }

            if (isHost.HasValue && isHost.Value)
            {
                if (playerManagerService.IsGameOver())
                {
                    isGameOver = true;
                    IGameNetworkMessage wsMessage = new GameOver();
                    SendToAllClients(wsMessage, DeliveryMethod.ReliableOrdered);
                    EventManager.OnGameOver(wsMessage as GameOver);

                    return;
                }

                SendLobbyUpdate();
            }
            else
            {
                IGameNetworkMessage playerUpdate = localPlayerUpdate;
                if (playerUpdate == null)
                {
                    Plugin.Log.LogWarning("Local player update is null, cannot send to host");
                    return;
                }
                // The local player already got the inventory above: leave it out of the message when the host has it
                if (!ShouldSendInventory(localPlayerUpdate.ConnectionId, localPlayerUpdate.Inventory))
                {
                    localPlayerUpdate.Inventory = null;
                }
                SendToHost(playerUpdate, DeliveryMethod.Unreliable);

                if (++ackUpdateCounter >= ACK_EVERY_UPDATES && enemyAckBuilder.TryBuild(out var latestEnemies, out var receivedEnemies))
                {
                    ackUpdateCounter = 0;
                    SendToHost(new EnemiesAck { ConnectionId = localPlayerUpdate.ConnectionId, Latest = latestEnemies, ReceivedMask = receivedEnemies }, DeliveryMethod.Unreliable);
                }
            }
        }

        public void UpdateEnemies()
        {
            if (isGameOver)
            {
                return;
            }

            if (!EnsureIsHost())
            {
                return;
            }

            SendEnemiesUpdate();
        }

        public void UpdateProjectiles()
        {
            if (isGameOver)
            {
                return;
            }

            if (!EnsureIsHost())
            {
                return;
            }

            SendProjectilesUpdate();
        }

        public void UpdateTumbleWeeds()
        {
            if (isGameOver)
            {
                return;
            }

            if (!EnsureIsHost())
            {
                return;
            }

            SendTumbleWeedsUpdate();
        }

        private void UpdateLocalPlayer(PlayerUpdate localPlayer)
        {
            var player = playerManagerService.GetLocalPlayer();

            if (player == null)
            {
                Plugin.Log.LogWarning("Local player is null");
                return;
            }

            if (localPlayer == null)
            {
                Plugin.Log.LogWarning("Local player update is null");
                return;
            }

            player.Position = Quantizer.Quantize(localPlayer.Position.ToUnityVector3());
            player.MovementState = localPlayer.MovementState;
            player.AnimatorState = localPlayer.AnimatorState;
            player.Hp = localPlayer.Hp;
            player.MaxHp = localPlayer.MaxHp;
            player.Shield = localPlayer.Shield;
            player.MaxShield = localPlayer.MaxShield;
            //player.Xp = localPlayer.Xp;
            player.Inventory = localPlayer.Inventory;
            player.Name = localPlayer.Name;

            playerManagerService.UpdatePlayer(player);
        }

        /// <summary>
        /// Whether a player's inventory must be in this update: it changed since the last one sent, or that one is old enough
        /// that it may have been lost. Records it as sent when true.
        /// </summary>
        private bool ShouldSendInventory(uint connectionId, InventoryInfo inventory)
        {
            if (inventory == null)
            {
                return false;
            }

            var now = UnityEngine.Time.realtimeSinceStartup;
            var forced = Environment.TickCount64 < Interlocked.Read(ref forceInventoriesUntilMs);
            if (!forced
                && sentInventories.TryGetValue(connectionId, out var sent)
                && now - sent.SentAt < INVENTORY_RESEND_INTERVAL_SECONDS
                && sent.Inventory.HasSameContent(inventory))
            {
                return false;
            }

            sentInventories[connectionId] = (inventory, now);
            return true;
        }

        /// <summary>
        /// A peer connected or a level changed: it may not have the inventories, send them in full for a moment
        /// </summary>
        public void ForceFullInventories()
        {
            Interlocked.Exchange(ref forceInventoriesUntilMs, Environment.TickCount64 + FORCE_INVENTORIES_MS);
        }

        private void SendLobbyUpdate()
        {
            // Copies without the inventories that did not change: the stored players keep theirs
            var players = playerManagerService.GetAllPlayers()
                .Select(player =>
                {
                    if (ShouldSendInventory(player.ConnectionId, player.Inventory))
                    {
                        return player;
                    }

                    var copy = player.ShallowCopy();
                    copy.Inventory = null;
                    return copy;
                })
                .ToList();

            var message = new LobbyUpdates
            {
                Players = players,
            };

            byte[] serialized = MemoryPackSerializer.Serialize<IGameNetworkMessage>(message);

            if (serialized.Length < MAX_PACKET_SIZE_BYTES)
            {
                SendToAllClients(serialized, DeliveryMethod.Unreliable);
                return;
            }

            SendSnapshotInChunks(players, chunk => new LobbyUpdates { Players = chunk });
        }

        /// <summary>
        /// Where the remote players look from, for the enemy distance tiers: each remote player, and when one of them is dead
        /// (it may spectate anyone) every living player, the host included. False when there is no remote player.
        /// Read from the stored players: no native call.
        /// </summary>
        private bool CollectEnemyReferencePositions(List<System.Numerics.Vector3> positions)
        {
            positions.Clear();
            var anyRemote = false;
            var anyRemoteDead = false;
            foreach (var player in playerManagerService.GetAllPlayersExceptLocal())
            {
                anyRemote = true;
                if (!positionReceivedPlayers.ContainsKey(player.ConnectionId))
                {
                    continue; //Default position is the world corner: every enemy would look far
                }
                positions.Add(Quantizer.Dequantize(player.Position).ToNumericsVector3());
                anyRemoteDead |= player.Hp == 0;
            }

            if (!anyRemote)
            {
                return false;
            }

            if (positions.Count == 0)
            {
                return true; //No remote position yet: empty list, every enemy is sent as near
            }

            if (anyRemoteDead)
            {
                foreach (var player in playerManagerService.GetAllPlayers())
                {
                    if (player.Hp > 0)
                    {
                        positions.Add(Quantizer.Dequantize(player.Position).ToNumericsVector3());
                    }
                }
            }

            return true;
        }

        private void SendEnemiesUpdate()
        {
            if (!CollectEnemyReferencePositions(enemyReferencePositions))
            {
                return; //Nobody to send enemies to
            }

            var enemies = enemyManagerService.GetAllEnemiesDeltaAndUpdate(enemyReferencePositions);
            var bossFinalOrb = finalBossOrbManagerService.GetAllOrbs().ToList();

            // One message per blob, merged into as few packets as possible by the end of frame batching
            // Each packet is numbered and remembered with its enemies: clients ack them, lost ones are resent absolute
            enemyBlobCounts.Clear();
            var blobs = EnemySnapshotCodec.Encode(enemies, ENEMY_SNAPSHOT_MAX_BYTES, enemyBlobCounts);
            var start = 0;
            for (var i = 0; i < blobs.Count; i++)
            {
                var ids = new uint[enemyBlobCounts[i]];
                for (var j = 0; j < ids.Length; j++)
                {
                    ids[j] = enemies[start + j].Id;
                }
                start += ids.Length;

                SendToAllClients(new EnemiesUpdate { Seq = enemySnapshotHistory.Record(ids), Data = blobs[i] }, DeliveryMethod.Unreliable);
            }

            if (bossFinalOrb.Count == 0)
            {
                return;
            }

            byte[] serialized = MemoryPackSerializer.Serialize<IGameNetworkMessage>(new LobbyUpdates { BossOrbs = bossFinalOrb });

            if (serialized.Length < MAX_PACKET_SIZE_BYTES)
            {
                SendToAllClients(serialized, DeliveryMethod.Unreliable);
                return;
            }

            SendSnapshotInChunks(bossFinalOrb, chunk => new LobbyUpdates { BossOrbs = chunk });
        }

        private void SendProjectilesUpdate()
        {
            var projectiles = projectileManagerService.GetAllProjectilesDeltaAndUpdate();

            if (projectiles.Count == 0)
            {
                return;
            }

            var message = new ProjectilesUpdate
            {
                Projectiles = projectiles,
            };

            byte[] serialized = MemoryPackSerializer.Serialize<IGameNetworkMessage>(message);

            if (serialized.Length < MAX_PACKET_SIZE_BYTES)
            {
                SendToAllClients(serialized, DeliveryMethod.Unreliable);
                return;
            }

            SendSnapshotInChunks(projectiles, chunk => new ProjectilesUpdate { Projectiles = chunk });
        }

        private void SendTumbleWeedsUpdate()
        {
            var tumbleWeeds = spawnedObjectManagerService.GetAllTumbleWeedsDeltaAndUpdate().ToList();

            if (tumbleWeeds.Count == 0)
            {
                return;
            }

            var message = new TumbleWeedsUpdate
            {
                TumbleWeeds = [.. tumbleWeeds],
            };

            byte[] serialized = MemoryPackSerializer.Serialize<IGameNetworkMessage>(message);

            if (serialized.Length < MAX_PACKET_SIZE_BYTES)
            {
                SendToAllClients(serialized, DeliveryMethod.Unreliable);
                return;
            }

            SendSnapshotInChunks(tumbleWeeds, chunk => new TumbleWeedsUpdate { TumbleWeeds = [.. chunk] });
        }

        /// <summary>
        /// Split a snapshot too big for one packet into several unreliable packets.
        /// It used to be sent ReliableOrdered instead, sharing the ordered queue with every other game message
        /// (pickups, xp, gold...): in late game the reliable window saturates and all of them get delayed by seconds.
        /// Receivers apply snapshot entries independently, so a partial snapshot is fine.
        /// </summary>
        private void SendSnapshotInChunks<TItem>(List<TItem> items, Func<List<TItem>, IGameNetworkMessage> createChunk)
        {
            foreach (var (data, fits) in SnapshotChunker.Serialize(items, createChunk, MAX_PACKET_SIZE_BYTES))
            {
                SendToAllClients(data, fits ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered);
            }
        }

        public void SendToAllClients<T>(T data, DeliveryMethod deliveryMethod) where T : IGameNetworkMessage
        {
            if (!EnsureIsHost())
            {
                return;
            }

            var msgBytes = MemoryPackSerializer.Serialize<IGameNetworkMessage>(data);

            if (usesRelay.Any())
            {
                SendOrQueueToRelay(new RelayTarget(null, null), msgBytes, deliveryMethod);
            }

            if (gamePeers.Count == 0)
            {
                //Plugin.Log.LogWarning("No other clients connected");
                return;
            }

            foreach (var (_, peer) in gamePeers)
            {
                SendOrQueue(peer, msgBytes, deliveryMethod);
            }
        }

        public void SendToHost<T>(T data, DeliveryMethod? overrideDeliveryMethod = null) where T : IGameNetworkMessage
        {
            if (!EnsureIsClient())
            {
                return;
            }

            var msgBytes = MemoryPackSerializer.Serialize<IGameNetworkMessage>(data);

            var deliveryMethod = overrideDeliveryMethod ?? DeliveryMethod.ReliableSequenced;

            if (msgBytes.Length >= MAX_PACKET_SIZE_BYTES)
            {
                deliveryMethod = DeliveryMethod.ReliableOrdered;
            }

            if (usesRelay.Any())
            {
                SendOrQueueToRelay(new RelayTarget(null, null), msgBytes, DeliveryMethod.ReliableOrdered);
                return;
            }

            if (gamePeers.Count == 0)
            {
                Plugin.Log.LogWarning("Not connected to host");
                return;
            }

            SendOrQueue(gamePeers[0], msgBytes, deliveryMethod);
        }

        public void SendToClient<T>(NetPeer client, T data, uint connectionId) where T : IGameNetworkMessage
        {
            if (!EnsureIsHost())
            {
                return;
            }

            var msgBytes = MemoryPackSerializer.Serialize<IGameNetworkMessage>(data);

            if (usesRelay.Contains(connectionId))
            {
                SendOrQueueToRelay(new RelayTarget(connectionId, null), msgBytes, DeliveryMethod.ReliableOrdered);
                return;
            }

            SendOrQueue(client, msgBytes, DeliveryMethod.ReliableOrdered);
        }

        private bool EnsureIsHost()
        {
            if (isHost == null)
            {
                Plugin.Log.LogWarning("IsHost not set yet");
                return false;
            }
            if (!isHost.Value)
            {
                Plugin.Log.LogWarning("Only host can perform this action");
                return false;
            }
            return true;
        }

        private bool EnsureIsClient()
        {
            if (isHost == null)
            {
                Plugin.Log.LogWarning("IsHost not set yet");
                return false;
            }
            if (isHost.Value)
            {
                Plugin.Log.LogWarning("Only client can perform this action");
                return false;
            }
            return true;
        }

        public bool? IsHost()
        {
            return isHost;
        }

        public bool HasAllPeersConnected()
        {
            return hasAllPeersConnected;
        }

        public int GetLatency(uint connectionId)
        {
            if (isHost.HasValue && isHost.Value)
            {
                var peerIntro = gamePeersIntroduced.FirstOrDefault(p => p.Value.ConnectionId == connectionId);
                if (peerIntro.Value != null)
                {
                    return peerIntro.Value.Latency;
                }

                var peerIntroByRelay = gamePeersIntroducedByRelay.FirstOrDefault(p => p.Value.ConnectionId == connectionId);
                if (peerIntroByRelay.Value != null)
                {
                    return peerIntroByRelay.Value.Latency;
                }
            }
            else
            {
                var peerIntro = gamePeersIntroduced.FirstOrDefault(p => p.Value.ConnectionId == connectionId);
                if (peerIntro.Value != null)
                {
                    return peerIntro.Value.Latency;
                }

                var peerIntroByRelay = gamePeersIntroducedByRelay.FirstOrDefault(p => p.Value.ConnectionId == connectionId);
                if (peerIntroByRelay.Value != null)
                {
                    return peerIntroByRelay.Value.Latency;
                }
            }

            return -1;
        }

        public void SendToAllClientsExcept<T>(int netPlayerId, uint sender, T data) where T : IGameNetworkMessage
        {
            if (!EnsureIsHost())
            {
                return;
            }

            var msgBytes = MemoryPackSerializer.Serialize<IGameNetworkMessage>(data);

            if (usesRelay.Any())
            {
                bool found = false;
                uint toExcept = 0;
                if (gamePeersIntroducedByRelay.ContainsKey(sender))
                {
                    toExcept = gamePeersIntroducedByRelay[sender].ConnectionId;
                    found = true;
                }

                if (toExcept == 0)
                {
                    var normalPeer = gamePeersIntroducedByRelay.FirstOrDefault(p => p.Value.ConnectionId == sender).Value;

                    if (normalPeer != null)
                    {
                        toExcept = normalPeer.ConnectionId;
                        found = true;
                    }
                }


                SendOrQueueToRelay(new RelayTarget(null, found ? toExcept : null), msgBytes, DeliveryMethod.ReliableOrdered);
            }

            if (gamePeers.Count == 0)
            {
                //Plugin.Log.LogWarning("No clients connected");
                return;
            }

            var filteredPeers = gamePeers.Where(p => p.Value.Id != netPlayerId);
            foreach (var (_, peer) in filteredPeers)
            {
                SendOrQueue(peer, msgBytes, DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>
        /// Delivery method used for a batch, false when the message must not be batched.
        /// Sequenced methods drop older packets: a dropped batch would lose every message in it, and ReliableSequenced
        /// only retransmits the latest packet. Since batching divides the packet count, ReliableSequenced batches go
        /// ReliableOrdered (every message arrives, in order). Unreliable Sequenced is not batched.
        /// </summary>
        private static bool TryGetBatchDeliveryMethod(DeliveryMethod deliveryMethod, out DeliveryMethod batchDeliveryMethod)
        {
            batchDeliveryMethod = deliveryMethod switch
            {
                DeliveryMethod.ReliableSequenced => DeliveryMethod.ReliableOrdered,
                _ => deliveryMethod
            };

            return deliveryMethod != DeliveryMethod.Sequenced;
        }

        private void SendOrQueue(NetPeer peer, byte[] msgBytes, DeliveryMethod deliveryMethod)
        {
            if (TryGetBatchDeliveryMethod(deliveryMethod, out var batchDeliveryMethod))
            {
                lock (pendingLock)
                {
                    if (isBatchingActive)
                    {
                        if (!pendingByPeer.TryGetValue((peer, batchDeliveryMethod), out var pending))
                        {
                            pending = [];
                            pendingByPeer[(peer, batchDeliveryMethod)] = pending;
                        }
                        pending.Add(msgBytes);
                        return;
                    }
                }
            }

            peer.Send(msgBytes, deliveryMethod);
        }

        private void SendOrQueueToRelay(RelayTarget target, byte[] msgBytes, DeliveryMethod deliveryMethod)
        {
            if (TryGetBatchDeliveryMethod(deliveryMethod, out var batchDeliveryMethod))
            {
                lock (pendingLock)
                {
                    if (isBatchingActive)
                    {
                        if (!pendingByRelay.TryGetValue(batchDeliveryMethod, out var pending))
                        {
                            pending = [];
                            pendingByRelay[batchDeliveryMethod] = pending;
                        }
                        pending.Add((target, msgBytes));
                        return;
                    }
                }
            }

            SendToRelay(target, msgBytes, deliveryMethod);
        }

        private void SendToRelay(RelayTarget target, byte[] payload, DeliveryMethod deliveryMethod)
        {
            RelayEnvelope relayEnvelope = new()
            {
                TargetConnectionId = target.TargetConnectionId ?? 0,
                HaveTarget = target.TargetConnectionId.HasValue,
                ToFilters = target.ExceptConnectionId.HasValue ? [target.ExceptConnectionId.Value] : [],
                Payload = payload,
            };
            var relayMsgBytes = MemoryPackSerializer.Serialize(relayEnvelope);
            lock (relayPeerLock)
            {
                relayPeer?.Send(relayMsgBytes, deliveryMethod);
            }
        }

        public void FlushPendingMessages()
        {
            lock (pendingLock)
            {
                isBatchingActive = isBatchFormatValid;
                FlushPendingMessagesLocked();
            }
        }

        private void FlushPendingMessagesLocked()
        {
            foreach (var ((peer, deliveryMethod), pending) in pendingByPeer)
            {
                if (pending.Count == 0)
                {
                    continue;
                }

                try
                {
                    var maxSize = Math.Min(MAX_PACKET_SIZE_BYTES, peer.GetMaxSinglePacketSize(deliveryMethod));
                    SendPacked(pending, maxSize, compressionPeerIds.ContainsKey(peer.Id), data => TrySendQueued(() => peer.Send(data, deliveryMethod)));
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to send queued messages: {ex.Message}");
                }
                finally
                {
                    pending.Clear();
                }
            }

            foreach (var (deliveryMethod, pending) in pendingByRelay)
            {
                if (pending.Count == 0)
                {
                    continue;
                }

                try
                {
                    int maxSize;
                    lock (relayPeerLock)
                    {
                        maxSize = relayPeer != null ? relayPeer.GetMaxSinglePacketSize(deliveryMethod) : MAX_PACKET_SIZE_BYTES;
                    }
                    maxSize = Math.Min(MAX_PACKET_SIZE_BYTES, maxSize) - RELAY_ENVELOPE_OVERHEAD_BYTES;
                    var relayCompression = AllRemotePlayersSupportCompression();

                    // One envelope per run of consecutive messages going to the same players, keeping the order
                    var start = 0;
                    while (start < pending.Count)
                    {
                        var target = pending[start].Target;
                        var end = start;
                        while (end < pending.Count && pending[end].Target == target)
                        {
                            end++;
                        }

                        var run = pending.GetRange(start, end - start).Select(p => p.Data).ToList();
                        SendPacked(run, maxSize, relayCompression, data => TrySendQueued(() => SendToRelay(target, data, deliveryMethod)));
                        start = end;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to send queued relay messages: {ex.Message}");
                }
                finally
                {
                    pending.Clear();
                }
            }
        }

        /// <summary>
        /// One packet failing (too big for an unreliable send...) must not drop the other packets of the queue
        /// </summary>
        private void TrySendQueued(Action send)
        {
            try
            {
                send();
            }
            catch (Exception ex)
            {
                failedQueuedSends++;
                if (failedQueuedSends <= 3 || failedQueuedSends % 200 == 0)
                {
                    Plugin.Log.LogError($"Failed to send a queued packet ({failedQueuedSends} so far): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Send the messages in order, as few packets as possible under maxSize. A message alone (or too big to share a packet)
        /// is sent as is.
        /// </summary>
        private static void SendPacked(List<byte[]> messages, int maxSize, bool compress, Action<byte[]> send)
        {
            var batch = new List<byte[]>();
            var batchSize = batchHeader.Length;

            foreach (var message in messages)
            {
                // The version check goes alone and raw, so a mismatched build still reads it
                var isVersionInfo = message.Length > 0 && message[0] == modVersionInfoTag;

                if (batch.Count > 0 && (isVersionInfo || batchSize + message.Length >= maxSize))
                {
                    send(MaybeCompress(PackBatch(batch, batchSize), compress));
                    batch.Clear();
                    batchSize = batchHeader.Length;
                }

                if (isVersionInfo)
                {
                    send(message);
                    continue;
                }

                batch.Add(message);
                batchSize += message.Length;
            }

            if (batch.Count > 0)
            {
                send(MaybeCompress(PackBatch(batch, batchSize), compress));
            }
        }

        /// <summary>
        /// Packets are built under the size limit uncompressed, so the compressed one (only used when smaller) fits too
        /// </summary>
        private static byte[] MaybeCompress(byte[] packet, bool compress)
        {
            if (!compress || !isCompressionAvailable)
            {
                return packet;
            }

            try
            {
                return PacketCompression.Compress(packet) ?? packet;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Packet compression failed, sent raw: {ex.Message}");
                return packet;
            }
        }

        private void OnPeerCapabilities(PeerCapabilities capabilities, int netPeerId)
        {
            if (!capabilities.SupportsCompression)
            {
                logger.LogWarning($"Player {capabilities.OwnerId} can't decompress packets: sending it uncompressed packets");
                return;
            }

            compressionConnectionIds[capabilities.OwnerId] = 0;
            bool isRelay;
            lock (relayPeerLock)
            {
                isRelay = relayPeer != null && relayPeer.Id == netPeerId;
            }
            if (!isRelay)
            {
                compressionPeerIds[netPeerId] = 0;
            }
        }

        /// <summary>
        /// Relay packets may go to any player: compressed only if every one of them said it can decode them
        /// </summary>
        private bool AllRemotePlayersSupportCompression()
        {
            var any = false;
            foreach (var player in playerManagerService.GetAllPlayersExceptLocal())
            {
                any = true;
                if (!compressionConnectionIds.ContainsKey(player.ConnectionId))
                {
                    return false;
                }
            }
            return any;
        }

        private static bool CheckCompression()
        {
            var available = PacketCompression.SelfTest();
            if (!available)
            {
                Plugin.Log.LogWarning("Packet compression unavailable: packets are sent uncompressed");
            }
            return available;
        }

        private static byte[] PackBatch(List<byte[]> messages, int batchSize)
        {
            if (messages.Count == 1)
            {
                return messages[0];
            }

            var packed = new byte[batchSize];
            Buffer.BlockCopy(batchHeader, 0, packed, 0, batchHeader.Length);
            BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(batchHeader.Length - sizeof(int)), messages.Count);

            var offset = batchHeader.Length;
            foreach (var message in messages)
            {
                Buffer.BlockCopy(message, 0, packed, offset, message.Length);
                offset += message.Length;
            }

            return packed;
        }

        /// <summary>
        /// Make sure a batch built by PackBatch reads back as MemoryPack would serialize it, else send every message on its own
        /// </summary>
        private static bool CheckBatchFormat()
        {
            try
            {
                var first = new AddXp { Xp = 1234, LeftOverXp = 0.5f, OwnerId = 7 };
                var second = new PingPlaced { OwnerId = 42, Position = new System.Numerics.Vector3(1f, 2f, 3f) };
                var firstBytes = MemoryPackSerializer.Serialize<IGameNetworkMessage>(first);
                var secondBytes = MemoryPackSerializer.Serialize<IGameNetworkMessage>(second);

                var packed = PackBatch([firstBytes, secondBytes], batchHeader.Length + firstBytes.Length + secondBytes.Length);
                var expected = MemoryPackSerializer.Serialize<IGameNetworkMessage>(new MessageBatch { Messages = [first, second] });

                if (packed.AsSpan().SequenceEqual(expected)
                    && MemoryPackSerializer.Deserialize<IGameNetworkMessage>(packed) is MessageBatch batch
                    && batch.Messages.Count == 2
                    && batch.Messages[0] is AddXp { Xp: 1234, OwnerId: 7 }
                    && batch.Messages[1] is PingPlaced { OwnerId: 42 })
                {
                    return true;
                }

                Plugin.Log.LogWarning("Message batching disabled: unexpected batch format");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Message batching disabled: {ex.Message}");
            }

            return false;
        }

        private void DropPendingMessages(NetPeer peer)
        {
            lock (pendingLock)
            {
                foreach (var key in pendingByPeer.Keys.Where(k => k.Peer == peer).ToList())
                {
                    pendingByPeer.Remove(key);
                }

                bool isRelayPeer;
                lock (relayPeerLock)
                {
                    isRelayPeer = relayPeer == peer;
                }
                if (isRelayPeer)
                {
                    pendingByRelay.Clear();
                }
            }
        }

        public void SendToAllClients(byte[] data, DeliveryMethod deliveryMethod)
        {
            if (!EnsureIsHost())
            {
                return;
            }

            if (usesRelay.Any())
            {
                RelayEnvelope relayEnvelope = new()
                {
                    Payload = data,
                };
                var relayMsgBytes = MemoryPackSerializer.Serialize(relayEnvelope);
                lock (relayPeerLock)
                {
                    relayPeer?.Send(relayMsgBytes, deliveryMethod);
                }
            }

            if (gamePeers.Count == 0)
            {
                //Plugin.Log.LogWarning("No clients connected");
                return;
            }

            NetDataWriter writer = new NetDataWriter();
            writer.Put(data);

            try
            {
                foreach (var (_, peer) in gamePeers)
                {
                    peer.Send(writer, deliveryMethod);
                }
            }
            catch (LiteNetLib.TooBigPacketException ex)
            {
                Plugin.Log.LogError($"Failed to send message: {ex.Message}");
            }
        }

        public void UpdateMode(bool isHost)
        {
            this.isHost = isHost;
        }

        public bool IsHandlingConnection()
        {
            return isHandlingConnection;
        }

        public void CancelAnyNatIntroduction()
        {
            pollingCancelationTokenSource?.Cancel();
        }

        public bool HasHandledHost()
        {
            return hasHandledHost;
        }

        public void RemovePeer(uint clientConnectionId)
        {
            var peerIntro = gamePeersIntroduced.FirstOrDefault(p => p.Value.ConnectionId == clientConnectionId);
            if (peerIntro.Value != null)
            {
                gamePeersIntroduced.Remove(peerIntro.Key, out _);
            }
            var peerIntroByRelay = gamePeersIntroducedByRelay.FirstOrDefault(p => p.Value.ConnectionId == clientConnectionId);
            if (peerIntroByRelay.Value != null)
            {
                gamePeersIntroducedByRelay.Remove(peerIntroByRelay.Key, out _);
            }
        }

        public void ResetHandledHost()
        {
            hasHandledHost = false;
        }

        public int GetLocalPort()
        {
            return GAME_UDP_PORT;
        }

        public int GetResetGeneration()
        {
            return resetGeneration;
        }

        public void RegisterHost(uint selfConnectionId, int seed, int resetGeneration)
        {
            // Runs on the matchmaking task: the user may have stopped (reset) or switched to joining meanwhile
            lock (registerHostLock)
            {
                var mode = Plugin.Instance.Mode;
                if (resetGeneration != this.resetGeneration || mode.Mode != NetworkModeType.Friendlies || mode.Role != Role.Host)
                {
                    Plugin.Log.LogInfo("Networking was reset or is no longer hosting: host not registered");
                    return;
                }

                this.selfConnectionId ??= selfConnectionId;
                isHost = true;

                if (playerManagerService.GetAllPlayers().Any(p => p.ConnectionId == selfConnectionId))
                {
                    return; // A join was already handled: the match info set the room's seed
                }

                playerManagerService.AddPlayer(selfConnectionId, true, true);
                playerManagerService.SetSeed(seed); // A join's match info brings the room's seed
                Plugin.Log.LogInfo($"Host registered in its room ({selfConnectionId})");
            }
        }

        public bool IsSoloSession()
        {
            var isHost = this.isHost;
            var selfConnectionId = this.selfConnectionId;
            if (isHost != true || !selfConnectionId.HasValue || Plugin.Instance.Mode.Mode != NetworkModeType.Friendlies)
            {
                return false;
            }

            if (isHandlingConnection || !gamePeers.IsEmpty || !gamePeersIntroduced.IsEmpty || !gamePeersIntroducedByRelay.IsEmpty || usesRelay.Count > 0)
            {
                return false;
            }

            // Members accepted by the room but not added as players yet
            matchmakingService ??= (IWebsocketClientService)Plugin.Services.GetService(typeof(IWebsocketClientService));
            if (matchmakingService != null && matchmakingService.HasJoiningPlayers())
            {
                return false;
            }

            var players = playerManagerService.GetAllPlayers().ToList();
            return players.Count == 1 && players[0].ConnectionId == selfConnectionId.Value;
        }
    }
}
