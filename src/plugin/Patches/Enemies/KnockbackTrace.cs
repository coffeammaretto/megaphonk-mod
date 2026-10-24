using Actors.Enemies;
using HarmonyLib;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MegabonkTogether.Patches.Enemies
{
    /// <summary>
    /// Diagnostic (2.0.6): host log of the enemy knockbacks, to compare the direction with the attacker's and the host's
    /// position. At most 4 lines a second for the host's own hits and 4 for the other players' hits.
    /// </summary>
    [HarmonyPatch]
    internal static class KnockbackTracePatches
    {
        private const float INTERVAL = 0.25f;
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();
        private static readonly ITrackerService trackerService = Plugin.Services.GetService<ITrackerService>();
        private static float nextLocal;
        private static float nextRemote;

        public static IEnumerable<MethodBase> TargetMethods()
        {
            var method = AccessTools.Method(typeof(EnemyMovementRb), nameof(EnemyMovementRb.Knockback), [typeof(Assets.Scripts.Actors.DamageContainer)]);
            if (method != null && NativeCodeGuard.IsSafeToPatch(method))
            {
                yield return method;
            }
        }

        public static void Prefix(EnemyMovementRb __instance, out Vector3 __state)
        {
            __state = __instance.knockbackVelocity;
        }

        public static void Postfix(EnemyMovementRb __instance, Assets.Scripts.Actors.DamageContainer __0, Vector3 __state)
        {
            var dc = __0;
            if (!synchronizationService.HasNetplaySessionStarted() || !(synchronizationService.IsServerMode() ?? false))
            {
                return;
            }

            var localId = playerManagerService.GetLocalPlayer()?.ConnectionId;
            var attackerId = trackerService.GetCurrentPlayerId() ?? DynamicDataOwner(dc) ?? localId;
            var remote = attackerId.HasValue && attackerId != localId;
            var now = Time.realtimeSinceStartup;
            if (remote ? now < nextRemote : now < nextLocal)
            {
                return;
            }
            if (remote) nextRemote = now + INTERVAL; else nextLocal = now + INTERVAL;

            var enemyPosition = __instance.transform.position;
            var host = GameManager.Instance?.player;
            var fromHost = host != null ? Flat(enemyPosition - host.transform.position) : Vector3.zero;
            var fromAttacker = Vector3.zero;
            if (remote)
            {
                var netPlayer = playerManagerService.GetNetPlayerByNetplayId(attackerId.Value);
                if (netPlayer?.Model != null)
                {
                    fromAttacker = Flat(enemyPosition - netPlayer.Model.transform.position);
                }
            }
            else
            {
                fromAttacker = fromHost;
            }

            var after = __instance.knockbackVelocity;
            var replay = Plugin.Instance.DAMAGE_BEING_REPLAYED != System.IntPtr.Zero && dc.Pointer == Plugin.Instance.DAMAGE_BEING_REPLAYED;
            Plugin.Log.LogInfo($"[Knockback] attacker={attackerId} remote={remote} replay={replay} src={dc.damageSource} kb={dc.knockback:F2} " +
                $"vel {__state.magnitude:F1}->{after.magnitude:F1} pushDir={Dir(after)} dcDir={Dir(dc.direction)} " +
                $"fromAttacker={Dir(fromAttacker)} {fromAttacker.magnitude:F1}m fromHost={Dir(fromHost)} {fromHost.magnitude:F1}m");
        }

        private static uint? DynamicDataOwner(Assets.Scripts.Actors.DamageContainer dc)
        {
            return MonoMod.Utils.DynamicData.For(dc).Get<uint?>("ownerId");
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static string Dir(Vector3 v)
        {
            v.y = 0f;
            var n = v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
            return $"({n.x:F2},{n.z:F2})";
        }
    }
}
