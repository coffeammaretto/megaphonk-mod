using Assets.Scripts.Actors.Enemies;
using System;
using System.Collections.Concurrent;

namespace MegabonkTogether.Helpers
{
    /// <summary>
    /// Player each enemy chases (its connection id), keyed by the enemy's native pointer.
    /// Used to live in a DynamicData field: a lookup per enemy per physics step. Pooled enemies keep their pointer,
    /// InitEnemy clears the entry before the new target is set, and everything is cleared with the level.
    /// </summary>
    public static class EnemyTargets
    {
        private static readonly ConcurrentDictionary<IntPtr, uint> targetIds = new();

        public static uint? Get(Enemy enemy)
        {
            if (enemy is null)
            {
                return null;
            }

            return targetIds.TryGetValue(enemy.Ptr(), out var targetId) ? targetId : null;
        }

        public static void Set(Enemy enemy, uint targetId)
        {
            targetIds[enemy.Ptr()] = targetId;
        }

        public static void Remove(Enemy enemy)
        {
            targetIds.TryRemove(enemy.Ptr(), out _);
        }

        public static void Clear()
        {
            targetIds.Clear();
        }
    }
}
