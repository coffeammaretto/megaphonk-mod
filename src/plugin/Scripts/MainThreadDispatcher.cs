using MegabonkTogether.Helpers;
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace MegabonkTogether.Scripts
{
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> _executionQueue = new();

        public void Update()
        {
            var perf = PerfProbe.Begin();
            while (_executionQueue.TryDequeue(out var action))
            {
                var actionPerf = PerfProbe.Begin(); //Per queued action (lambda named after its enclosing method, e.g. the received message's event)
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(ex);
                }
                PerfProbe.End(action.Method, actionPerf);
            }
            PerfProbe.End("MainThreadDispatcher", perf);
        }

        public static void Enqueue(Action action)
        {
            _executionQueue.Enqueue(action);
        }
    }
}
