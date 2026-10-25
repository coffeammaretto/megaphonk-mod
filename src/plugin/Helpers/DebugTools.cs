using Assets.Scripts.Actors;
using Assets.Scripts.Managers;
using HarmonyLib;
using Megaphonk.Configuration;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using UnityEngine;

namespace Megaphonk.Helpers
{
    /// <summary>
    /// Testing shortcuts, off unless Diagnostics.DebugTools is set (on every PC of the party):
    /// the first stage's portal leads to the final boss stage, and F10 on the host kills every enemy (final boss included)
    /// </summary>
    [HarmonyPatch]
    internal static class DebugTools
    {
        private static bool wasKillKeyDown;

        public static bool Enabled => ModConfig.DebugTools.Value;

        /// <summary>
        /// Leaving the first stage: the final boss stage instead of the second one. Every machine takes the stage
        /// transition, so the party follows (the lobby waits for everyone to load)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapController), nameof(MapController.LoadNextStage))]
        public static bool LoadNextStage_Prefix()
        {
            if (!Enabled || MapController.index != 0)
            {
                return true;
            }

            Plugin.Log.LogInfo("Debug tools: first stage portal, loading the final boss stage");
            MapController.LoadFinalStage();
            return false;
        }

        /// <summary>
        /// Every frame with the network ticks
        /// </summary>
        public static void Tick()
        {
            if (!Enabled)
            {
                return;
            }

            var isKillKeyDown = BepInEx.Unity.IL2CPP.UnityEngine.Input.GetKeyInt((BepInEx.Unity.IL2CPP.UnityEngine.KeyCode)KeyCode.F10);
            var isKillKeyPressed = isKillKeyDown && !wasKillKeyDown;
            wasKillKeyDown = isKillKeyDown;

            if (!isKillKeyPressed || !(Plugin.Services.GetService<IUdpClientService>().IsHost() ?? false))
            {
                return;
            }

            var enemies = Plugin.Services.GetService<IEnemyManagerService>().GetAllSpawnedEnemies();
            Plugin.Log.LogInfo($"Debug tools: killing {enemies.Count} enemies");
            foreach (var enemy in enemies)
            {
                try
                {
                    if (enemy == null || enemy.IsDead())
                    {
                        continue;
                    }

                    // The host's damage path: deaths, drops and kills reach the clients as usual
                    enemy.Damage(new DamageContainer(0f, "Megaphonk debug")
                    {
                        damage = enemy.maxHp * 100f + 1000000f,
                        enemy = enemy
                    });
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Debug tools: could not kill an enemy: {ex.Message}");
                }
            }
        }
    }
}
