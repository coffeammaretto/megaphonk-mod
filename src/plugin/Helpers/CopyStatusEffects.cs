using Assets.Scripts.Inventory__Items__Pickups.Stats;
using System;

namespace Megaphonk.Helpers
{
    /// <summary>
    /// Status effects of teammate copies (their PlayerInventory on this machine). The game's "effect added/removed" events
    /// are static and don't say whose effect it is: every listener (HUD, audio filters, powerup visuals, enemies) acts as if
    /// it was the local player's. A copy's effect ending removed the local player's shield bubble or rage sound early.
    /// </summary>
    internal static class CopyStatusEffects
    {
        /// <summary>
        /// Runs an action on a copy's inventory without the static status effect events
        /// </summary>
        public static void Run(PlayerInventory copy, Action<PlayerInventory> action)
        {
            var added = PlayerStatusEffects.A_StatusEffectAdded;
            var removed = PlayerStatusEffects.A_StatusEffectRemoved;
            PlayerStatusEffects.A_StatusEffectAdded = null;
            PlayerStatusEffects.A_StatusEffectRemoved = null;
            try
            {
                action(copy);
            }
            finally
            {
                PlayerStatusEffects.A_StatusEffectAdded = added;
                PlayerStatusEffects.A_StatusEffectRemoved = removed;
            }
        }
    }
}
