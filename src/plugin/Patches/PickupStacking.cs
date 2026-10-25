using Assets.Scripts.Inventory__Items__Pickups.Pickups;
using HarmonyLib;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Megaphonk.Patches
{
    /// <summary>
    /// Over its limit of xp or gold objects (ModConfig.MaxPickupObjects) the game merges the oldest one into the next
    /// (PickupStackableList.CombineOldestObjects: next.AddValue(oldest), then the oldest goes back to the pool). The
    /// oldest one's network ids move to the one that now carries its value, so the others still find it
    /// </summary>
    [HarmonyPatch]
    internal static class PickupStackingPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPickupManagerService pickupManagerService = Plugin.Services.GetService<IPickupManagerService>();

        public static IEnumerable<MethodBase> TargetMethods()
        {
            return new[] { AccessTools.Method(typeof(Pickup), nameof(Pickup.AddValue), [typeof(Pickup)]) }
                .Where(method => method != null && NativeCodeGuard.IsSafeToPatch(method));
        }

        public static void Prefix(Pickup __instance, Pickup __0)
        {
            if (__0 == null || !synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            pickupManagerService.MovePickupIds(__0, __instance);
            DynamicData.For(__0).Data.Clear(); //Pooled: nothing of it must follow the next pickup using this object
        }
    }
}
