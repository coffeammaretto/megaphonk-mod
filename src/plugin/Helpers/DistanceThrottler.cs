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
        private readonly Dictionary<int, float> lastUpdateTimes = [];
        private readonly float mediumDistanceUpdateInterval = mediumDistanceUpdateInterval;

        public bool ShouldUpdate(GameObject gameObject, int instanceId, bool isServer = false)
        {
            DistanceToPlayer distance = Plugin.GetDistanceToPlayer(gameObject.transform.position);
            return ShouldUpdate(instanceId, distance, isServer);
        }

        private bool ShouldUpdate(int instanceId, DistanceToPlayer distance, bool isServer = false)
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

        public void Cleanup(int instanceId)
        {
            lastUpdateTimes.Remove(instanceId);
        }

        public void ClearAll()
        {
            lastUpdateTimes.Clear();
        }
    }
}
