using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using HarmonyLib;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Enemies;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegabonkTogether.Patches.Enemies
{
    [HarmonyPatch(typeof(Enemy))]
    internal static class EnemyPatch
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();
        private static readonly IEnemyManagerService enemyManagerService = Plugin.Services.GetService<IEnemyManagerService>();
        private static readonly ITrackerService trackerService = Plugin.Services.GetService<ITrackerService>();

        public static readonly HashSet<string> AllowedDamageSource = new(Enum.GetNames(typeof(EItem)));

        /// <summary>
        /// Client damage triggered by its own player getting hit, which only the client sees (the host never hits a net
        /// player): Thorns retaliation, and the shockwave of an Aegis block (its copy on the host doesn't run)
        /// </summary>
        private static readonly HashSet<string> ClientHitDamageSource = ["Thorns", "Aegis"];

        public static readonly DistanceThrottler EnemiesDistanceThrottler = new();

        /// <summary>
        /// Client side, per enemy pointer: boss flag and transform for the throttled MyUpdate/MyFixedUpdate (cleared by InitEnemy)
        /// </summary>
        private static readonly Dictionary<IntPtr, (bool IsBoss, Transform Transform, EEnemyFlag Flag)> clientThrottleInfos = [];
        private static readonly HashSet<IntPtr> spawnAppliedEnemies = [];

        /// <summary>
        /// Level change / back to menu: forget everything keyed by enemy pointers
        /// </summary>
        public static void ResetCaches()
        {
            EnemiesDistanceThrottler.ClearAll();
            clientThrottleInfos.Clear();
            spawnAppliedEnemies.Clear();
            EnemyTargets.Clear();
        }

        /// <summary>
        /// Randomly target a player (Not necessarly the local player)
        /// Also adds TargetSwitcher component to the enemy for target switching
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Enemy.InitEnemy))]
        public static void init_PostFix(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            var pointer = __instance.Ptr();
            EnemiesDistanceThrottler.Cleanup(pointer);
            clientThrottleInfos.Remove(pointer);
            spawnAppliedEnemies.Remove(pointer);
            EnemyTargets.Remove(__instance); //Pooled enemy: the previous life's target is set again below (host) or by the spawn message (client)
            var renderer = __instance.GetComponentInChildren<Renderer>();
            if (renderer != null && !renderer.enabled)
            {
                renderer.enabled = true;
            }

            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && isServer.Value)
            {
                // Spawned next to a player: chase that one. Bosses, minibosses and other enemies spawned at a fixed spot
                // used to chase the host until their first target switch (20-60 s): pick a random alive player instead
                var host = playerManagerService.GetLocalPlayer();
                var spawnTargetId = SpawnPositionsPatches.TakeSpawnTarget() ?? playerManagerService.GetRandomPlayerAliveConnectionId() ?? host.ConnectionId;
                var targetNetPlayer = spawnTargetId != host.ConnectionId ? playerManagerService.GetNetPlayerByNetplayId(spawnTargetId) : null;

                if (targetNetPlayer != null)
                {
                    __instance.target = targetNetPlayer.Rigidbody;
                    EnemyTargets.Set(__instance, targetNetPlayer.ConnectionId);
                }
                else
                {
                    EnemyTargets.Set(__instance, host.ConnectionId);
                }

                // Pooled enemies go through InitEnemy on every spawn: reuse the switcher (StartSwitching resets it) instead of
                // stacking one more each time
                var gameObject = __instance.gameObject;
                var switcher = gameObject.GetComponent<TargetSwitcher>();
                if (switcher is null || switcher.CachedPtr() == IntPtr.Zero)
                {
                    switcher = gameObject.AddComponent<TargetSwitcher>();
                }

                if (__instance.enemyData.enemyName == Actors.Enemies.EEnemy.GhostGrave1
                    || __instance.enemyData.enemyName == Actors.Enemies.EEnemy.GhostGrave2
                    || __instance.enemyData.enemyName == Actors.Enemies.EEnemy.GhostGrave3
                    || __instance.enemyData.enemyName == Actors.Enemies.EEnemy.GhostGrave4)
                {
                    var targetId = switcher.StartSwitching(__instance, true);

                    EnemyTargets.Set(__instance, targetId);
                }
                else
                {
                    switcher.StartSwitching(__instance);
                }

                enemyManagerService.InitializeSwitcher(switcher, __instance.enemyFlag, __instance.enemyData.enemyName);
            }
            else
            {
                __instance.basePowerupDropChance = 0f;
                __instance.minStayAtDistance = 0f;
            }
        }

        /// <summary>
        /// Manually register a local kill to properly track item procs, money flying and stats
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.EnemyDied), typeof(DamageContainer))]
        public static void EnemyDied_Prefix(Enemy __instance, DamageContainer dc)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            var killerId = GetKillerId(dc);
            if (killerId.HasValue && killerId.Value != playerManagerService.GetLocalPlayer().ConnectionId) { return; }

            trackerService.RegisterTrack();
        }

        /// <summary>
        /// Player credited for a kill (money flying, on kill item procs): the one whose damage is being applied, else the
        /// owner of the weapon, since the host also simulates the clients' weapons
        /// </summary>
        private static uint? GetKillerId(DamageContainer dc)
        {
            var currentTracker = trackerService.GetCurrentPlayerId();
            if (currentTracker.HasValue || dc == null)
            {
                return currentTracker;
            }

            return DynamicData.For(dc).Get<uint?>("ownerId");
        }

        /// <summary>
        /// Synchronize enemy death
        /// </summary>

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Enemy.EnemyDied), typeof(DamageContainer))]
        public static void EnemyDied_Postfix(Enemy __instance, DamageContainer dc)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (!Plugin.CAN_SEND_MESSAGES)
            {
                return;
            }

            synchronizationService.OnEnemyDied(__instance, dc, GetKillerId(dc));
        }

        /// <summary>
        /// Manually register a local kill to properly track item procs, money flying and stats
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.EnemyDied), [])]
        public static void EnemyDiedWithoutDc_Prefix(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            var currentTracker = trackerService.GetCurrentPlayerId();
            if (currentTracker.HasValue && currentTracker.Value != playerManagerService.GetLocalPlayer().ConnectionId) { return; }

            trackerService.RegisterTrack();
        }

        /// <summary>
        /// Same as above 
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Enemy.EnemyDied), new System.Type[0])]
        public static void EnemyDiedWithoutDc_Postfix(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (!Plugin.CAN_SEND_MESSAGES)
            {
                return;
            }

            synchronizationService.OnEnemyDied(__instance, ownerId: trackerService.GetCurrentPlayerId());
        }

        /// <summary>
        /// Damage applied server-side only
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.Damage))]
        public static bool Damage_Prefix(Enemy __instance, DamageContainer damageContainer)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (isServer)
            {
                return true;
            }

            // Only the host's damage being replayed. Anything it triggers (item procs) is blocked here and forwarded by the postfix
            return IsReplayedDamage(damageContainer);
        }

        private static bool IsReplayedDamage(DamageContainer damageContainer)
        {
            return Plugin.Instance.DAMAGE_BEING_REPLAYED != IntPtr.Zero && damageContainer.Pointer == Plugin.Instance.DAMAGE_BEING_REPLAYED;
        }

        /// <summary>
        /// Synchronize enemy damage to all clients, synchronize item damage for client
        /// </summary>

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Enemy.Damage))]
        public static void Damage_Postfix(Enemy __instance, DamageContainer damageContainer)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (isServer)
            {
                synchronizationService.OnEnemyDamaged(__instance, damageContainer);
                return;
            }

            // Client: forward its own item damage and the damage of being hit, never the host's damage being replayed: sending
            // that back made the host apply it again and echo it again, until the enemy died.
            if (IsReplayedDamage(damageContainer))
            {
                return;
            }

            var damageSource = damageContainer.damageSource; //Native string read: once, passed along
            if (AllowedDamageSource.Contains(damageSource) || ClientHitDamageSource.Contains(damageSource))
            {
                synchronizationService.OnEnemyDamaged(__instance, damageContainer, damageSource);
            }
        }

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.TeleportToPlayer))]
        public static bool TeleportToPlayer_Prefix(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.StartTeleporting))]
        public static bool StartTeleporting_Prefix(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prevent clients from updating enemy movement
        /// </summary> 
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.TryTeleport))]
        public static bool TryTeleport_Prefix(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Heal applied server-side only
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.Heal))]
        public static bool Heal_Prefix(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        ///  Prevent enemies from running away from the player in netplay
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.IsRunningFromPlayer))]
        public static bool IsRunningFromPlayer_Prefix(ref bool __result)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            __result = false;

            return false;
        }

        /// <summary>
        /// Prevent enemies from being stationary in netplay
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.IsStationary))]
        public static bool IsStationary_Prefix(ref bool __result)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }
            __result = false;
            return false;
        }

        /// <summary>
        /// Distance-based throttling for non boss enemies
        /// clients skip far enemies and update medium ones once a second, the host updates all of them
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.MyUpdate))]
        public static bool MyUpdate_Prefix(Enemy __instance)
        {
            return ShouldUpdateEnemy(__instance);
        }

        /// <summary>
        /// Distance-based throttling for non boss enemies
        /// clients skip far enemies and update medium ones once a second, the host updates all of them
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Enemy.MyFixedUpdate))]
        public static bool MyFixedUpdate_Prefix(Enemy __instance)
        {
            return ShouldUpdateEnemy(__instance);
        }

        private static bool ShouldUpdateEnemy(Enemy __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (isServer)
            {
                return true; //The host updates every enemy, boss or not, at any distance
            }

            // Boss flag and transform fetched once per spawn, each one is a native call. Only cached once the spawn message
            // was fully applied (MarkSpawnApplied), and recomputed if the enemy flag (a plain field read) changes since
            var pointer = __instance.Ptr();
            var flag = __instance.enemyFlag;
            if (!clientThrottleInfos.TryGetValue(pointer, out var info)
                || info.Flag != flag
                || info.Transform is null
                || info.Transform.CachedPtr() == IntPtr.Zero)
            {
                info = (__instance.IsBoss() || __instance.IsStageBoss() || __instance.IsFinalBoss(), __instance.gameObject.transform, flag);
                if (spawnAppliedEnemies.Contains(pointer))
                {
                    clientThrottleInfos[pointer] = info;
                }
            }

            if (info.IsBoss)
            {
                return true;
            }

            return EnemiesDistanceThrottler.ShouldUpdate(info.Transform, pointer, isServer);
        }

        /// <summary>
        /// Cleanup throttler tracking when enemy dies
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Enemy.EnemyDied), typeof(DamageContainer))]
        public static void EnemyDied_Cleanup(Enemy __instance)
        {
            var pointer = __instance.Ptr();
            EnemiesDistanceThrottler.Cleanup(pointer);
            clientThrottleInfos.Remove(pointer);
            spawnAppliedEnemies.Remove(pointer);
        }

        /// <summary>
        /// Client: the spawn message was fully applied to this enemy (flags, hp, boss assignments), its boss flag can be cached
        /// </summary>
        public static void MarkSpawnApplied(Enemy enemy)
        {
            var pointer = enemy.Ptr();
            clientThrottleInfos.Remove(pointer);
            spawnAppliedEnemies.Add(pointer);
        }
    }
}
