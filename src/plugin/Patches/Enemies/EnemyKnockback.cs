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
}
