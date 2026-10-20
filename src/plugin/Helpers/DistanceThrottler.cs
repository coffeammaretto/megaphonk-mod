using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegabonkTogether.Helpers
{
    /// <summary>
    /// Updates far objects less often on clients (their position comes from the host's snapshots anyway).
    /// Objects always stay rendered: hiding them past a distance made enemies pop in only when a player came close.
    /// </summary>
    public class DistanceThrottler(float mediumDistanceUpdateInterval = 1f)
    {
        // Keyed by the object's native pointer (pooled objects keep it, GetInstanceID is a native call)
        private readonly Dictionary<IntPtr, float> lastUpdateTimes = [];
        private readonly float mediumDistanceUpdateInterval = mediumDistanceUpdateInterval;

        public bool ShouldUpdate(Transform transform, IntPtr instanceId, bool isServer = false)
        {
            DistanceToPlayer distance = Plugin.GetDistanceToPlayer(transform.position);
            return ShouldUpdate(instanceId, distance, isServer);
        }

        private bool ShouldUpdate(IntPtr instanceId, DistanceToPlayer distance, bool isServer = false)
        {
            switch (distance)
            {
                case DistanceToPlayer.Far:
                    return isServer;

                case DistanceToPlayer.Medium:
                    if (isServer)
                    {
                        return true;
                    }

                    float currentTime = Time.time;
                    if (!lastUpdateTimes.TryGetValue(instanceId, out float lastTime) ||
                        currentTime - lastTime >= mediumDistanceUpdateInterval)
                    {
                        lastUpdateTimes[instanceId] = currentTime;
                        return true;
                    }

                    return false;

                case DistanceToPlayer.Close:
                default:
                    return true;
            }
        }

        public void Cleanup(IntPtr instanceId)
        {
            lastUpdateTimes.Remove(instanceId);
        }

        public void ClearAll()
        {
            lastUpdateTimes.Clear();
        }
    }
}
