using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MegabonkTogether.Helpers
{
    /// <summary>
    /// Diagnostic, off by default (Diagnostics.PerformanceLog): main thread time spent in the mod. Every Harmony patch method
    /// of the mod is wrapped with a timer, plus explicit sections (network ticks, dispatcher). Times are inclusive: a patch
    /// that triggers another patched game method also counts the nested one (the mod total counts only the outermost).
    /// Tiny patch methods inlined by the JIT are missed.
    /// Logs a report every 10 s and the mod's share of each frame slower than the spike threshold.
    /// </summary>
    internal static class PerfProbe
    {
        private const string HARMONY_ID = MyPluginInfo.PLUGIN_GUID + ".perf";
        private const float REPORT_INTERVAL = 10f;
        private const int REPORT_TOP = 15;
        private const int SPIKE_TOP = 6;
        private const int MAX_SPIKES_PER_REPORT = 8;

        private sealed class Entry
        {
            public string Name;
            public long FrameTicks;
            public long WindowTicks;
            public int WindowCalls;
            public long WindowMaxTicks;
            public bool Touched;
        }

        public static bool Enabled { get; private set; }

        private static ManualLogSource log;
        private static int mainThreadId;
        private static float spikeThresholdMs;
        private static int depth;
        private static readonly Dictionary<IntPtr, Entry> entriesByMethod = [];
        private static readonly Dictionary<string, Entry> entriesByName = [];
        private static readonly List<Entry> entries = [];
        private static readonly List<Entry> touchedThisFrame = [];

        private static long frameModTicks;
        private static long lastGcIndex;
        private static double framePauseMs;
        private static double windowPauseMs;
        private static int frameGen0;
        private static int frameGen1;
        private static int frameGen2;

        private static float windowElapsed;
        private static int windowFrames;
        private static float windowMaxFrameMs;
        private static int windowSpikes;
        private static int windowSpikesLogged;
        private static long windowModTicks;
        private static int windowGen0;
        private static int windowGen1;
        private static int windowGen2;
        private static long windowAllocated;

        private static double MsPerTick => 1000.0 / Stopwatch.Frequency;

        /// <summary>
        /// Called once on the main thread after every patch of the mod is applied
        /// </summary>
        public static void Initialize(Harmony modHarmony, ManualLogSource logger, float spikeThreshold)
        {
            log = logger;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            spikeThresholdMs = spikeThreshold;

            var probeHarmony = new Harmony(HARMONY_ID);
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(PerfProbe), nameof(ProbePrefix)));
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(PerfProbe), nameof(ProbeFinalizer)));

            var patchMethods = new HashSet<MethodInfo>();
            foreach (var original in modHarmony.GetPatchedMethods().ToList())
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null)
                {
                    continue;
                }

                foreach (var patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                {
                    if (patch.owner == modHarmony.Id)
                    {
                        patchMethods.Add(patch.PatchMethod);
                    }
                }
            }

            var wrapped = 0;
            foreach (var method in patchMethods)
            {
                try
                {
                    var entry = new Entry { Name = $"{method.DeclaringType?.Name}.{method.Name}" };
                    entriesByMethod[method.MethodHandle.Value] = entry;
                    entries.Add(entry);
                    probeHarmony.Patch(method, prefix: prefix, finalizer: finalizer);
                    wrapped++;
                }
                catch (Exception ex)
                {
                    log.LogWarning($"[Perf] Could not wrap {method.DeclaringType?.Name}.{method.Name}: {ex.Message}");
                }
            }

            ResetWindow();
            Enabled = true;
            log.LogInfo($"[Perf] Performance log enabled: {wrapped} patch methods timed, spikes above {spikeThresholdMs:F0} ms. " +
                $".NET GC: server={System.Runtime.GCSettings.IsServerGC}, latency={System.Runtime.GCSettings.LatencyMode}");
        }

        private static void ProbePrefix(out long __state)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                __state = 0;
                return;
            }

            depth++;
            __state = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Finalizer, not postfix: runs when the patch throws too, so the depth stays right. Void: the exception is rethrown.
        /// </summary>
        private static void ProbeFinalizer(MethodBase __originalMethod, long __state)
        {
            if (__state == 0)
            {
                return;
            }

            depth--;
            if (entriesByMethod.TryGetValue(__originalMethod.MethodHandle.Value, out var entry))
            {
                Add(entry, Stopwatch.GetTimestamp() - __state);
            }
        }

        /// <summary>
        /// Start of an explicit section, pass the result to End
        /// </summary>
        public static long Begin()
        {
            if (!Enabled || Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                return 0;
            }

            depth++;
            return Stopwatch.GetTimestamp();
        }

        public static void End(string name, long start)
        {
            if (start == 0)
            {
                return;
            }

            depth--;
            if (!entriesByName.TryGetValue(name, out var entry))
            {
                entry = new Entry { Name = name };
                entriesByName[name] = entry;
                entries.Add(entry);
            }

            Add(entry, Stopwatch.GetTimestamp() - start);
        }

        /// <summary>
        /// End of an explicit section named after a method (service ticks)
        /// </summary>
        public static void End(MethodInfo method, long start)
        {
            if (start == 0)
            {
                return;
            }

            depth--;
            var key = method.MethodHandle.Value;
            if (!entriesByMethod.TryGetValue(key, out var entry))
            {
                entry = new Entry { Name = $"{method.DeclaringType?.Name}.{method.Name}" };
                entriesByMethod[key] = entry;
                entries.Add(entry);
            }

            Add(entry, Stopwatch.GetTimestamp() - start);
        }

        private static void Add(Entry entry, long ticks)
        {
            if (!entry.Touched)
            {
                entry.Touched = true;
                touchedThisFrame.Add(entry);
            }

            entry.FrameTicks += ticks;
            entry.WindowTicks += ticks;
            entry.WindowCalls++;
            if (ticks > entry.WindowMaxTicks)
            {
                entry.WindowMaxTicks = ticks;
            }

            if (depth == 0)
            {
                frameModTicks += ticks;
                windowModTicks += ticks;
            }
        }

        /// <summary>
        /// Once per frame, first thing of NetworkHandler.Update: closes the previous frame (unscaledDeltaTime is its length)
        /// </summary>
        public static void OnFrame(float unscaledDeltaTime)
        {
            if (!Enabled)
            {
                return;
            }

            var frameMs = unscaledDeltaTime * 1000f;
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            if (gen0 != frameGen0)
            {
                // Pause of the last .NET collection (several in one frame: only the last one is counted)
                var info = GC.GetGCMemoryInfo(GCKind.Any);
                if (info.Index != lastGcIndex)
                {
                    lastGcIndex = info.Index;
                    foreach (var pause in info.PauseDurations)
                    {
                        framePauseMs += pause.TotalMilliseconds;
                    }
                    windowPauseMs += framePauseMs;
                }
            }

            windowFrames++;
            windowElapsed += unscaledDeltaTime;
            windowMaxFrameMs = Math.Max(windowMaxFrameMs, frameMs);

            if (frameMs >= spikeThresholdMs)
            {
                windowSpikes++;
                if (windowSpikesLogged < MAX_SPIKES_PER_REPORT)
                {
                    windowSpikesLogged++;
                    LogSpike(frameMs, gen0 - frameGen0, gen1 - frameGen1, gen2 - frameGen2);
                }
            }

            foreach (var entry in touchedThisFrame)
            {
                entry.FrameTicks = 0;
                entry.Touched = false;
            }
            touchedThisFrame.Clear();
            frameModTicks = 0;
            framePauseMs = 0;
            depth = 0; //Safety: an unbalanced section must not hide the mod total for the rest of the session
            frameGen0 = gen0;
            frameGen1 = gen1;
            frameGen2 = gen2;

            if (windowElapsed >= REPORT_INTERVAL)
            {
                LogReport(gen0, gen1, gen2);
                ResetWindow();
            }
        }

        private static void LogSpike(float frameMs, int gen0, int gen1, int gen2)
        {
            var sb = new StringBuilder();
            sb.Append($"[Perf] Spike {frameMs:F0} ms: mod {frameModTicks * MsPerTick:F1} ms, .NET GC {gen0}/{gen1}/{gen2} (gen0/1/2) pause {framePauseMs:F1} ms");
            foreach (var entry in touchedThisFrame.OrderByDescending(e => e.FrameTicks).Take(SPIKE_TOP))
            {
                sb.Append($" | {entry.Name} {entry.FrameTicks * MsPerTick:F1}");
            }
            log.LogInfo(sb.ToString());
        }

        private static void LogReport(int gen0, int gen1, int gen2)
        {
            var seconds = Math.Max(windowElapsed, 0.001f);
            var allocatedMb = (GC.GetTotalAllocatedBytes(false) - windowAllocated) / (1024.0 * 1024.0);
            var sb = new StringBuilder();
            sb.AppendLine($"[Perf] {seconds:F1} s: {windowFrames / seconds:F0} fps avg, worst frame {windowMaxFrameMs:F0} ms, " +
                $"{windowSpikes} frames >= {spikeThresholdMs:F0} ms. Mod {windowModTicks * MsPerTick / seconds:F1} ms/s " +
                $"({windowModTicks * MsPerTick / Math.Max(windowFrames, 1):F2} ms/frame). " +
                $".NET GC {gen0 - windowGen0}/{gen1 - windowGen1}/{gen2 - windowGen2}, pause {windowPauseMs:F1} ms, allocated {allocatedMb:F1} MB");
            foreach (var entry in entries.Where(e => e.WindowCalls > 0).OrderByDescending(e => e.WindowTicks).Take(REPORT_TOP))
            {
                sb.AppendLine($"[Perf]   {entry.WindowTicks * MsPerTick / seconds,7:F2} ms/s  {entry.WindowCalls / seconds,8:F1} calls/s  " +
                    $"max {entry.WindowMaxTicks * MsPerTick,6:F2} ms  {entry.Name}");
            }
            log.LogInfo(sb.ToString().TrimEnd());
        }

        private static void ResetWindow()
        {
            foreach (var entry in entries)
            {
                entry.WindowTicks = 0;
                entry.WindowCalls = 0;
                entry.WindowMaxTicks = 0;
            }

            windowElapsed = 0f;
            windowFrames = 0;
            windowMaxFrameMs = 0f;
            windowSpikes = 0;
            windowSpikesLogged = 0;
            windowModTicks = 0;
            windowPauseMs = 0;
            windowGen0 = GC.CollectionCount(0);
            windowGen1 = GC.CollectionCount(1);
            windowGen2 = GC.CollectionCount(2);
            windowAllocated = GC.GetTotalAllocatedBytes(false);
        }
    }
}
