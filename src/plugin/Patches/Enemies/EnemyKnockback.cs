using Actors.Enemies;
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
    /// Host: the game pushes an enemy away from the local player (DamageContainer.direction = enemy - player). For a hit by
    /// another player's weapon, simulated on the host, that is away from the host: push it away from that player instead.
    /// </summary>
    [HarmonyPatch]
    internal static class EnemyKnockbackDirectionPatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();
        private static readonly Services.IPlayerManagerService playerManagerService = Plugin.Services.GetService<Services.IPlayerManagerService>();
        private static readonly Services.ITrackerService trackerService = Plugin.Services.GetService<Services.ITrackerService>();

        public static IEnumerable<MethodBase> TargetMethods()
        {
            var method = AccessTools.Method(typeof(EnemyMovementRb), nameof(EnemyMovementRb.Knockback), [typeof(Assets.Scripts.Actors.DamageContainer)]);
            if (method != null && NativeCodeGuard.IsSafeToPatch(method))
            {
                yield return method;
            }
        }

        public static void Prefix(EnemyMovementRb __instance, Assets.Scripts.Actors.DamageContainer __0)
        {
            if (__0 == null || !synchronizationService.HasNetplaySessionStarted() || !(synchronizationService.IsServerMode() ?? false))
            {
                return;
            }

            var attackerId = trackerService.GetCurrentPlayerId() ?? MonoMod.Utils.DynamicData.For(__0).Get<uint?>("ownerId");
            if (!attackerId.HasValue || !playerManagerService.IsRemoteConnectionId(attackerId.Value))
            {
                return;
            }

            var netPlayer = playerManagerService.GetNetPlayerByNetplayId(attackerId.Value);
            if (netPlayer?.Model == null)
            {
                return;
            }

            var direction = __instance.transform.position - netPlayer.Model.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                __0.direction = direction.normalized;
            }
        }
    }
}
