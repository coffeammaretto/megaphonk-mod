using Assets.Scripts.Actors.Enemies;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MegabonkTogether.Scripts.Enemies
{
    public class TargetSwitcher : MonoBehaviour
    {
        private Enemy enemy;
        private float timer = 0f;
        private float delay = 0f;
        private float switchMaxDistance = 100f;
        private (Transform transform, Rigidbody rigidBody) currentTarget = (null, null);

        private uint currentTargetNetplayId = 0;
        private (float min, float max) switchIntervalRange = (2f, 6f);
        private IPlayerManagerService playerManagerService;
        private ISynchronizationService synchronizationService;

        private void Awake()
        {
            playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();
            synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        }

        /// <summary>
        /// Called on every spawn: pooled enemies keep their switcher, so everything starts again as on a new component
        /// </summary>
        public uint StartSwitching(Enemy targetEnemy, bool pickACloseTarget = false)
        {
            enemy = targetEnemy;
            switchMaxDistance = 100f;
            switchIntervalRange = (2f, 6f);
            currentTarget = (null, null);
            currentTargetNetplayId = 0;
            ResetTimer();

            if (pickACloseTarget)
            {
                PickACloseTarget();
            }
            else
            {
                PickANewTarget();
            }

            return currentTargetNetplayId;
        }

        public void UpdateSwitchIntervalRange(float minSeconds, float maxSeconds)
        {
            switchIntervalRange = (minSeconds, maxSeconds);
        }

        public void UpdateSwitchMaxDistance(float distance)
        {
            switchMaxDistance = distance;
        }

        private void PickANewTarget()
        {
            var alives = playerManagerService.GetAllPlayersAlive().ToList();
            if (alives.Count == 0) return;

            var selectedPlayer = alives[Random.Range(0, alives.Count)];

            if (playerManagerService.IsRemoteConnectionId(selectedPlayer.ConnectionId))
            {
                var netplayer = playerManagerService.GetNetPlayerByNetplayId(selectedPlayer.ConnectionId);
                currentTarget = (netplayer.Model.transform, netplayer.Rigidbody);
            }
            else
            {
                currentTarget = (GameManager.Instance.player.transform, GameManager.Instance.player.playerMovement.rb);
            }

            currentTargetNetplayId = selectedPlayer.ConnectionId;
        }

        private void PickACloseTarget()
        {
            var alives = playerManagerService.GetAllPlayersAlive().ToList();
            if (alives.Count == 0) return;

            var closestDistance = float.MaxValue;
            (Transform transform, Rigidbody rigidBody) closestTarget = (null, null);
            
            uint closestNetplayId = 0;
            foreach (var player in alives)
            {
                (Transform transform, Rigidbody rigidBody) target;
                if (playerManagerService.IsRemoteConnectionId(player.ConnectionId))
                {
                    var netplayer = playerManagerService.GetNetPlayerByNetplayId(player.ConnectionId);
                    target = (netplayer.Model.transform, netplayer.Rigidbody);
                }
                else
                {
                    target = (GameManager.Instance.player.transform, GameManager.Instance.player.playerMovement.rb);
                }

                var distance = Vector3.Distance(enemy.transform.position, target.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestTarget = target;
                    closestNetplayId = player.ConnectionId;
                }
            }
            currentTarget = closestTarget;
            currentTargetNetplayId = closestNetplayId;
        }

        private void Update()
        {
            if (enemy is null || enemy.CachedPtr() == IntPtr.Zero) return;

            timer += NetworkHandler.FrameDeltaTime;
            if (timer >= delay)
            {
                if (!synchronizationService.HasNetplaySessionStarted()) return;

                PickANewTarget();
                if (currentTarget.transform == null)
                {
                    ResetTimer();
                    return;
                }
                if (CanSwitch())
                {
                    EnemyTargets.Set(enemy, currentTargetNetplayId);
                    enemy.target = currentTarget.rigidBody;
                }
                ResetTimer();
            }
        }

        private bool CanSwitch()
        {
            if (enemy.transform == null) return false;

            float distance = Vector3.Distance(enemy.transform.position, currentTarget.transform.position);
            return distance <= switchMaxDistance;
        }

        private void ResetTimer()
        {
            timer = 0f;
            delay = Random.Range(switchIntervalRange.min, switchIntervalRange.max);
        }
    }
}
