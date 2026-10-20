using Assets.Scripts.Inventory__Items__Pickups.Weapons.Projectiles;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Helpers;
using UnityEngine;

namespace MegabonkTogether.Extensions
{
    public static class ProjectileBaseExtensions
    {
        public static Projectile ToModel(this ProjectileBase projectile, uint projectileId)
        {
            var transform = projectile.GetSnapshotTransform();
            return ToModel(projectileId, transform.position, transform.rotation);
        }

        /// <summary>
        /// Transform whose position and rotation are synchronized: the moving part for swords and rockets.
        /// Resolve it once per projectile, it costs three GetComponent calls.
        /// </summary>
        public static Transform GetSnapshotTransform(this ProjectileBase projectile)
        {
            var projectileCringeSword = projectile.GetComponent<ProjectileCringeSword>();
            var projectileHeroSword = projectile.GetComponent<ProjectileHeroSword>();
            var projectileRocket = projectile.GetComponent<ProjectileRocket>();

            if (projectileRocket != null)
            {
                return projectileRocket.rocket.transform;
            }

            if (projectileHeroSword != null)
            {
                return projectileHeroSword.movingProjectile.transform;
            }

            if (projectileCringeSword != null)
            {
                return projectileCringeSword.movingProjectile.transform;
            }

            return projectile.transform;
        }

        public static Projectile ToModel(uint projectileId, Vector3 position, Quaternion rotation)
        {
            return new Projectile()
            {
                Id = projectileId,
                Position = Quantizer.Quantize(position),
                FordwardVector = Quantizer.Quantize(rotation * Vector3.forward)
            };
        }
    }
}
