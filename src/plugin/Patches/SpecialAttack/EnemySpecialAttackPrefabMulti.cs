using HarmonyLib;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using static EnemySpecialAttackPrefabMulti;

namespace Megaphonk.Patches.SpecialAttack
{
    [HarmonyPatch(typeof(_DoAttack_d__7))]
    internal class EnemySpecialAttackPrefabMultiPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// Intercept projectile to target the correct player instead of the orignal function targeting always the local player.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(_DoAttack_d__7.MoveNext))]
        public static void MoveNext_Prefix(_DoAttack_d__7 __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }
            var targetId = Megaphonk.Helpers.EnemyTargets.Get(__instance.__4__this.enemy);
            if (targetId.HasValue)
            {
                playerManagerService.AddGetNetplayerPositionRequest(targetId.Value);
            }
        }

        /// <summary>
        /// Remove queued request after attack is over.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(_DoAttack_d__7.MoveNext))]
        public static void MoveNext_Postfix(_DoAttack_d__7 __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }
            var targetId = Megaphonk.Helpers.EnemyTargets.Get(__instance.__4__this.enemy);
            if (targetId.HasValue)
            {
                playerManagerService.UnqueueNetplayerPositionRequest();
            }
        }
    }
}
