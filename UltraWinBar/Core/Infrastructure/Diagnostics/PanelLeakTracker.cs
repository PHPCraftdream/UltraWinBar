using System;
using System.Collections.Generic;

namespace UltraWinBar.Utilities
{
    // K19: instead of a new regex per leak source, remember every closed Taskbar as a WeakReference
    // and let HealthReporter ask, at its rare GC-forcing interval, which of them are still alive.
    // Any missed unsubscribe (Settings, SystemEvents, TasksService, StartMenuMonitor, a timer, a
    // collection view) keeps its Taskbar rooted and shows up in the log without a profiler.
    internal static class PanelLeakTracker
    {
        // Panels close rarely (settings/display changes, Explorer restart); this bounds memory
        // without ever needing to trim in practice.
        private const int MaxTracked = 32;

        private static readonly object gate = new object();
        private static readonly List<(WeakReference Reference, string Label)> tracked = new List<(WeakReference, string)>();

        internal static void TrackClosed(Taskbar taskbar)
        {
            if (taskbar == null) return;
            string label = $"{taskbar.AppBarEdge}@{taskbar.Screen?.DeviceName ?? "?"}";
            Track(taskbar, label);
        }

        // Generic entry point so the accounting logic is testable without a real Taskbar.
        internal static void Track(object panel, string label)
        {
            if (panel == null) return;
            lock (gate)
            {
                tracked.Add((new WeakReference(panel), label));
                if (tracked.Count > MaxTracked)
                {
                    tracked.RemoveRange(0, tracked.Count - MaxTracked);
                }
            }
        }

        // Rare call (health snapshot only): forces a full GC pass, then reports which tracked
        // closed panels are still alive and drops the ones that were collected. Never call this
        // from a hot path.
        internal static string CollectAndReport()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            List<string> alive = new List<string>();
            lock (gate)
            {
                for (int i = tracked.Count - 1; i >= 0; i--)
                {
                    if (tracked[i].Reference.IsAlive)
                    {
                        alive.Add(tracked[i].Label);
                    }
                    else
                    {
                        tracked.RemoveAt(i);
                    }
                }
            }

            if (alive.Count == 0) return "0";
            alive.Reverse();
            return $"{alive.Count} ({string.Join(", ", alive)})";
        }

        // Test hook: current tracked count without forcing a GC pass.
        internal static int TrackedCount { get { lock (gate) return tracked.Count; } }
    }
}
