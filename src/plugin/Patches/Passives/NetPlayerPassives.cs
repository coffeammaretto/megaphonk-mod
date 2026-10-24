using Assets.Scripts.Inventory__Items__Pickups.AbilitiesPassive;
using Assets.Scripts.Inventory__Items__Pickups.AbilitiesPassive.Implementations;
using HarmonyLib;
using MegabonkTogether.Patches.Items;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Reflection;

namespace MegabonkTogether.Patches.Passives
{
    /// <summary>
    /// Character passives are written for a single player, like items (see ItemOwnership): the copies of the other players'
    /// passives listen to the same static events and reacted to the local player's (someone's Flex blocked the local
    /// player's hits, someone's Shadowstep executed on the local evades, someone's Gamba rolled on the local level up).
    /// Their owner runs them and sends the damage they deal.
    /// Plague is left on every copy: poison is applied by the host, so the host's copy is the one that spreads it.
    /// </summary>
    [HarmonyPatch]
    internal static class NetPlayerPassivePatches
    {
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        public static IEnumerable<MethodBase> TargetMethods()
        {
            return ItemOwnership.Safe(
            [
                AccessTools.Method(typeof(PassiveAbilityFlex), nameof(PassiveAbilityFlex.OnCheckStopDamage)),
                AccessTools.Method(typeof(PassiveAbilityShadowstep), nameof(PassiveAbilityShadowstep.OnEvade)),
                AccessTools.Method(typeof(PassiveAbilitySpeedDemon), nameof(PassiveAbilitySpeedDemon.OnDamage)),
                AccessTools.Method(typeof(PassiveAbilityLockIn), nameof(PassiveAbilityLockIn.OnAegisChange)),
                AccessTools.Method(typeof(PassiveAbilityGamba), nameof(PassiveAbilityGamba.OnLevelup)),
                AccessTools.Method(typeof(PassiveAbilityZooma), nameof(PassiveAbilityZooma.OnEnemyDamage)),
            ]);
        }

        public static bool Prefix(PassiveAbility __instance)
        {
            return !ItemOwnership.IsNetplay || !playerManagerService.IsNetPlayerPassive(__instance);
        }
    }
}
