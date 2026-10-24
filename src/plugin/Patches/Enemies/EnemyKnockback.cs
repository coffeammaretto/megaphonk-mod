using Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups.Weapons.Projectiles;
using HarmonyLib;
using MegabonkTogether.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MegabonkTogether.Patches.Enemies
{
    /// <summary>
    /// Clients show the host's enemies from snapshots, and their enemy movement update (which also damps a knockback) never
    /// runs: a knockback applied locally (the host's hits are replayed on clients) would push the enemy's rigidbody away
    /// until the next snapshot pulls it back. Only the host knocks enemies back.
    /// </summary>
    [HarmonyPatch]
    internal static class EnemyKnockbackPatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();

        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase[] candidates =
            [
                AccessTools.Method(typeof(EnemyMovementRb), nameof(EnemyMovementRb.Knockback), [typeof(Assets.Scripts.Actors.DamageContainer)]),
                AccessTools.Method(typeof(EnemyMovementRb), nameof(EnemyMovementRb.Knockback), [typeof(UnityEngine.Vector3), typeof(float)]),
                AccessTools.Method(typeof(EnemyMovementRb), nameof(EnemyMovementRb.KnockUp)),
            ];

            return candidates.Where(method => method != null && NativeCodeGuard.IsSafeToPatch(method));
        }

        public static bool Prefix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            return synchronizationService.IsServerMode() ?? true;
        }
    }

    /// <summary>
    /// Host: the owner of a knockback given with a DamageContainer, read by EnemyKnockbackDirectionPatches (which only gets
    /// the direction). Knockback(DamageContainer) calls Knockback(Vector3, float) directly.
    /// </summary>
    [HarmonyPatch]
    internal static class EnemyKnockbackOwnerPatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();

        public static uint? CurrentOwnerId { get; private set; }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            var method = AccessTools.Method(typeof(EnemyMovementRb), nameof(EnemyMovementRb.Knockback), [typeof(Assets.Scripts.Actors.DamageContainer)]);
            if (method != null && NativeCodeGuard.IsSafeToPatch(method))
            {
                yield return method;
            }
        }

        public static void Prefix(Assets.Scripts.Actors.DamageContainer __0)
        {
            CurrentOwnerId = __0 != null && synchronizationService.HasNetplaySessionStarted() && (synchronizationService.IsServerMode() ?? false)
                ? MonoMod.Utils.DynamicData.For(__0).Get<uint?>("ownerId")
                : null;
        }

        public static void Finalizer()
        {
            CurrentOwnerId = null;
        }
    }

    /// <summary>
    /// Host: the projectile hitting an enemy (every projectile hit is simulated on the host), read by
    /// EnemyKnockbackDirectionPatches
    /// </summary>
    [HarmonyPatch]
    internal static class EnemyKnockbackProjectilePatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();

        public static ProjectileBase CurrentProjectile { get; private set; }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            var method = AccessTools.Method(typeof(ProjectileBase), nameof(ProjectileBase.HitEnemy));
            if (method != null && NativeCodeGuard.IsSafeToPatch(method))
            {
                yield return method;
            }
        }

        public static void Prefix(ProjectileBase __instance)
        {
            CurrentProjectile = synchronizationService.HasNetplaySessionStarted() && (synchronizationService.IsServerMode() ?? false)
                ? __instance
                : null;
        }

        public static void Finalizer()
        {
            CurrentProjectile = null;
        }
    }

    /// <summary>
    /// Host: a projectile pushes the enemy along its flight direction; every other knockback (explosions, items, auras,
    /// melee) pushes it straight away from the player who hit it. The game takes the direction from the hit itself: an
    /// explosion's center or a hit from behind pushed the enemy into the player. Only the horizontal direction changes,
    /// the force and lift are kept.
    /// </summary>
    [HarmonyPatch]
    internal static class EnemyKnockbackDirectionPatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();
        private static readonly Services.IPlayerManagerService playerManagerService = Plugin.Services.GetService<Services.IPlayerManagerService>();
        private static readonly Services.ITrackerService trackerService = Plugin.Services.GetService<Services.ITrackerService>();

        public static IEnumerable<MethodBase> TargetMethods()
        {
            var method = AccessTools.Method(typeof(EnemyMovementRb), nameof(EnemyMovementRb.Knockback), [typeof(UnityEngine.Vector3), typeof(float)]);
            if (method != null && NativeCodeGuard.IsSafeToPatch(method))
            {
                yield return method;
            }
        }

        public static void Prefix(EnemyMovementRb __instance, ref UnityEngine.Vector3 __0)
        {
            if (!synchronizationService.HasNetplaySessionStarted() || !(synchronizationService.IsServerMode() ?? false))
            {
                return;
            }

            var horizontalForce = new UnityEngine.Vector2(__0.x, __0.z).magnitude;
            if (horizontalForce < 0.0001f)
            {
                return;
            }

            UnityEngine.Vector3 push;
            var projectile = EnemyKnockbackProjectilePatches.CurrentProjectile;
            if (projectile.IsAlive())
            {
                push = GetFlightDirection(projectile);
            }
            else if (TryGetAttackerPosition(out var attackerPosition))
            {
                push = __instance.transform.position - attackerPosition;
            }
            else
            {
                return;
            }

            push.y = 0f;
            if (push.sqrMagnitude < 0.0001f)
            {
                return;
            }

            push = push.normalized * horizontalForce;
            __0 = new UnityEngine.Vector3(push.x, __0.y, push.z);
        }

        /// <summary>
        /// Where the projectile is going: its movement direction, else its aim, else its facing
        /// </summary>
        private static UnityEngine.Vector3 GetFlightDirection(ProjectileBase projectile)
        {
            var direction = projectile.GetMovementDirection();
            direction.y = 0f;
            if (direction.sqrMagnitude >= 0.0001f)
            {
                return direction;
            }

            direction = projectile.direction;
            direction.y = 0f;
            return direction.sqrMagnitude >= 0.0001f ? direction : projectile.transform.forward;
        }

        /// <summary>
        /// The player who hit: replayed client hit or host-simulated projectile of a teammate (tracker), teammate weapon
        /// ticking on the host (position request), owner of the DamageContainer, else the local player
        /// </summary>
        private static bool TryGetAttackerPosition(out UnityEngine.Vector3 position)
        {
            var attackerId = trackerService.GetCurrentPlayerId()
                ?? playerManagerService.PeakNetplayerPositionRequest()
                ?? EnemyKnockbackOwnerPatches.CurrentOwnerId;

            if (attackerId.HasValue && playerManagerService.IsRemoteConnectionId(attackerId.Value))
            {
                var netPlayer = playerManagerService.GetNetPlayerByNetplayId(attackerId.Value);
                if (netPlayer?.Model != null)
                {
                    position = netPlayer.Model.transform.position;
                    return true;
                }
            }

            // A pending request for a teammate redirects the local player's transform to that teammate's model
            var pending = playerManagerService.PeakNetplayerPositionRequest();
            var player = GameManager.Instance?.player;
            if (player != null && (pending == null || !playerManagerService.IsRemoteConnectionId(pending.Value)))
            {
                position = player.transform.position;
                return true;
            }

            position = default;
            return false;
        }
    }
}
