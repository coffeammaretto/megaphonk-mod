using HarmonyLib;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace Megaphonk.Patches
{
    [HarmonyPatch(typeof(PlayerMovement))]
    internal static class PlayerMovementPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// Reset world size when teleporting player back to bounds (used when leaving graveyard boss room)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerMovement.TeleportPlayerBackToBounds))]
        public static void TeleportPlayerBackToBounds_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            Plugin.Instance.SetWorldSize(Plugin.Instance.OriginalWorldSize);
        }

        /// <summary>
        /// Prevent player movement when dead
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerMovement.MovementTick))]
        public static bool MovementTick_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (GameManager.Instance?.player?.IsDead() is true)
            {
                PlayerMovement.Instance.rb.velocity = Vector3.zero;
                PlayerMovement.Instance.currentMoveSpeed = 0f;

                return false;
            }

            return true;
        }

        private const float DOWNED_MAX_HORIZONTAL_SPEED = 1.5f;
        private static IDownedService downedService;

        /// <summary>
        /// A downed player barely crawls: cap the horizontal speed after the game moved the player
        /// (a move speed stat penalty didn't slow the actual movement down)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PlayerMovement.MovementTick))]
        public static void MovementTick_Postfix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            downedService ??= Plugin.Services.GetService<IDownedService>();
            if (downedService == null || !downedService.IsLocalDowned)
            {
                return;
            }

            var rb = PlayerMovement.Instance?.rb;
            if (rb == null)
            {
                return;
            }

            var velocity = rb.velocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            if (horizontal.magnitude > DOWNED_MAX_HORIZONTAL_SPEED)
            {
                horizontal = horizontal.normalized * DOWNED_MAX_HORIZONTAL_SPEED;
                rb.velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
            }
        }
    }
}
