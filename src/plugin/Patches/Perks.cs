using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Inventory__Items__Pickups.Weapons.Projectiles;
using Assets.Scripts.Utility;
using HarmonyLib;
using Megaphonk.Common.Messages.GameNetworkMessages;
using Megaphonk.Helpers;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System;
using UnityEngine;

namespace Megaphonk.Patches
{
    /// <summary>
    /// Hooks of the pact and curse mechanics (PerkService). A weapon hit is GetDamageContainer (with a projectile) then
    /// Enemy.Damage with that container: the host changes its damage (mark, bad luck) and runs the hit perks after it.
    /// </summary>
    [HarmonyPatch]
    internal static class PerkPatches
    {
        private const float RECOIL_FORCE = 6f;
        private const float RECOIL_COOLDOWN = 0.35f;
        private const float MISFIRE_CHANCE = 0.1f;
        private const float BLOOD_PRICE_SHARE = 0.005f;
        private const float SHORT_RANGE_SHARE = 0.3f;

        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();
        private static readonly ITrackerService trackerService = Plugin.Services.GetService<ITrackerService>();
        private static readonly IPerkService perkService = Plugin.Services.GetService<IPerkService>();
        private static readonly System.Random random = new();

        /// <summary>
        /// The weapon hit about to be applied: its container (the game recycles one), target, owner and frame
        /// </summary>
        private static IntPtr pendingHitDc;
        private static IntPtr pendingHitEnemy;
        private static uint pendingHitOwner;
        private static int pendingHitFrame = -1;
        private static WeaponBase misfiring;
        private static float nextRecoilAt;

        private static bool IsActive => synchronizationService.HasNetplaySessionStarted();

        private static bool IsLocalWeapon(WeaponBase weapon)
        {
            return weapon != null && playerManagerService.GetNetPlayerByWeapon(weapon) == null
                && GameManager.Instance?.player?.inventory?.weaponInventory?.weapons?.ContainsValue(weapon) == true;
        }

        private static uint? GetOwner(WeaponBase weapon)
        {
            var netPlayer = playerManagerService.GetNetPlayerByWeapon(weapon);
            if (netPlayer != null)
            {
                return netPlayer.ConnectionId;
            }

            return IsLocalWeapon(weapon) ? perkService.LocalId : null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WeaponUtility), nameof(WeaponUtility.GetDamageContainer), [typeof(WeaponBase), typeof(ProjectileBase), typeof(Enemy), typeof(Vector3), typeof(float)])]
        public static void GetDamageContainer_Postfix(WeaponBase weaponBase, Enemy enemy, DamageContainer __result)
        {
            pendingHitDc = IntPtr.Zero;
            if (!IsActive || !perkService.IsHost || __result == null || enemy == null)
            {
                return;
            }

            var owner = GetOwner(weaponBase);
            if (owner.HasValue)
            {
                pendingHitDc = __result.Pointer;
                pendingHitEnemy = enemy.Ptr();
                pendingHitOwner = owner.Value;
                pendingHitFrame = Time.frameCount;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Enemy), nameof(Enemy.Damage))]
        public static void Damage_Prefix(Enemy __instance, DamageContainer damageContainer, out long __state)
        {
            __state = -1; //The hit's owner, for the postfix: nested damage can arm another hit meanwhile
            if (!IsActive || pendingHitDc == IntPtr.Zero || damageContainer == null || damageContainer.Pointer != pendingHitDc
                || pendingHitFrame != Time.frameCount || __instance.Ptr() != pendingHitEnemy)
            {
                return;
            }

            pendingHitDc = IntPtr.Zero; //One hit per container
            __state = pendingHitOwner;
            try
            {
                damageContainer.damage = perkService.ModifyHitDamage(__instance, damageContainer.damage, pendingHitOwner);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Perk hit damage failed: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Enemy), nameof(Enemy.Damage))]
        public static void Damage_Postfix(Enemy __instance, DamageContainer damageContainer, long __state)
        {
            if (__state < 0 || damageContainer == null)
            {
                return;
            }

            try
            {
                perkService.OnWeaponHit(__instance, damageContainer.damage, damageContainer.direction, (uint)__state);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Perk hit failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Every death (EnemyDied() ends in this one)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Enemy), nameof(Enemy.EnemyDied), typeof(DamageContainer))]
        public static void EnemyDied_Postfix(Enemy __instance, DamageContainer dc)
        {
            OnEnemyDied(__instance, dc);
        }

        private static void OnEnemyDied(Enemy enemy, DamageContainer dc)
        {
            if (!IsActive || enemy == null)
            {
                return;
            }

            try
            {
                var killer = trackerService.GetCurrentPlayerId() ?? (dc == null ? null : DynamicData.For(dc).Get<uint?>("ownerId"));
                perkService.OnEnemyDied(enemy, killer);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Perk death failed: {ex.Message}");
            }
        }

        /// <summary>
        /// The local player's shot: misfire (the cooldown is spent, the attack doesn't come), recoil, blood price, echo
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WeaponBase), nameof(WeaponBase.Use))]
        public static void Use_Prefix(WeaponBase __instance)
        {
            misfiring = null;
            if (!IsActive)
            {
                return;
            }

            var local = perkService.LocalId;
            var misfire = perkService.GetStrength(local, Perk.Misfire);
            var recoil = perkService.GetStrength(local, Perk.Recoil);
            var bloodPrice = perkService.GetStrength(local, Perk.BloodPrice);
            if ((misfire <= 0f && recoil <= 0f && bloodPrice <= 0f && perkService.GetStrength(local, Perk.Echo) <= 0f) || !IsLocalWeapon(__instance))
            {
                return;
            }

            if (misfire > 0f && random.NextDouble() < Math.Min(MISFIRE_CHANCE * misfire, 0.5f))
            {
                misfiring = __instance;
                return;
            }

            var player = GameManager.Instance.player;
            if (recoil > 0f && Time.time >= nextRecoilAt)
            {
                var camera = GameManager.Instance.playerCamera?.camera;
                if (camera != null)
                {
                    nextRecoilAt = Time.time + RECOIL_COOLDOWN;
                    var back = -camera.transform.forward;
                    back.y = 0f;
                    player.playerMovement?.PushPlayer(back.normalized * RECOIL_FORCE * recoil);
                }
            }

            var health = player.inventory?.playerHealth;
            if (bloodPrice > 0f && health != null && health.hp > 1)
            {
                var cost = Math.Max(1, (int)(health.maxHp * BLOOD_PRICE_SHARE * bloodPrice));
                health.hp = Math.Max(1, health.hp - cost); //Never kills, no hit taken (no invulnerability frames)
            }

            perkService.QueueEcho(__instance);
        }

        /// <summary>
        /// Also when Use throws: a stuck misfire would block that weapon's attacks
        /// </summary>
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(WeaponBase), nameof(WeaponBase.Use))]
        public static void Use_Finalizer()
        {
            misfiring = null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WeaponUtility), nameof(WeaponUtility.WeaponAttack))]
        public static bool WeaponAttack_Prefix(WeaponBase weapon)
        {
            return misfiring == null || weapon != misfiring;
        }

        /// <summary>
        /// Short range: the local player's projectiles expire sooner (their copies end with them)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ProjectileBase), nameof(ProjectileBase.Set))]
        public static void Set_Postfix(ProjectileBase __instance, WeaponBase weaponBase)
        {
            if (!IsActive)
            {
                return;
            }

            var shortRange = perkService.GetStrength(perkService.LocalId, Perk.ShortRange);
            if (shortRange <= 0f || !IsLocalWeapon(weaponBase))
            {
                return;
            }

            var left = __instance.expirationTime - MyTime.time;
            if (left > 0f)
            {
                __instance.expirationTime = MyTime.time + left * Math.Max(0.3f, 1f - SHORT_RANGE_SHARE * shortRange);
            }
        }
    }
}
