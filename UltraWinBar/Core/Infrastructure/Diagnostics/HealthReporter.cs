using ManagedShell.Common.Logging;
using ManagedShell.Common.Native;
using ManagedShell.WindowsTasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    // Periodic resource snapshot in the regular log, so a leak over a long uptime is visible
    // from a user's log alone (no profiler, no dump).
    internal sealed class HealthReporter : IDisposable
    {
        private static readonly TimeSpan FirstReport = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
        private const uint GR_GDIOBJECTS = 0;
        private const uint GR_USEROBJECTS = 1;

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr process, uint flags);

        private readonly Tasks tasks;
        private readonly ManagedShellLogger logger;
        private readonly UiThreadLatencyMonitor latencyMonitor = new UiThreadLatencyMonitor();
        private readonly DispatcherTimer timer;
        private readonly DateTime started = DateTime.UtcNow;

        public HealthReporter(Tasks tasks, ManagedShellLogger logger = null)
        {
            this.tasks = tasks;
            this.logger = logger;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstReport };
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            timer.Interval = Interval;
            try
            {
                // Н12: rare liveness sweep piggybacks on this existing low-frequency timer.
                tasks?.SweepGhosts();
                ShellLogger.Info(Snapshot());
            }
            catch (Exception error) { ShellLogger.Warning($"Health: snapshot failed: {error.Message}"); }
        }

        // Fields are a flat list so another parallel change (e.g. a "ghost windows" counter) can
        // append one line without touching the formatting.
        internal string Snapshot()
        {
            using var process = Process.GetCurrentProcess();
            int windows = tasks?.GroupedWindows?.SourceCollection is ICollection source ? source.Count : -1;
            string uptimeHours = (DateTime.UtcNow - started).TotalHours.ToString("F1", CultureInfo.InvariantCulture);
            var latency = latencyMonitor.ConsumeSnapshot();
            long lostLogLines = (logger?.LostLogLines ?? 0) + (logger?.DroppedQueuedLogLines ?? 0);

            List<string> fields = new List<string>
            {
                $"uptime={uptimeHours}h",
                $"handles={process.HandleCount}",
                $"gdi={GetGuiResources(process.Handle, GR_GDIOBJECTS)}",
                $"user={GetGuiResources(process.Handle, GR_USEROBJECTS)}",
                $"threads={process.Threads.Count}",
                $"privateMB={process.PrivateMemorySize64 / (1024 * 1024)}",
                $"gcHeapMB={GC.GetTotalMemory(false) / (1024 * 1024)}",
                $"windows={windows}",
                $"winEventHooks={WinEventHook.InstalledCount}",
                $"mouseHooks={LowLevelMouseHook.InstalledCount}",
                $"settingsSubscribers={Settings.Instance.PropertyChangedSubscriberCount}",
                $"uiMaxDelayMs={latency.MaxMs}",
                $"uiDelayOver250={latency.WarnCount}",
                $"uiDelayOver1000={latency.SevereCount}",
                $"callbackFailures={CallbackGuard.TotalFailureCount}",
                $"comProxiesDown={ShellComProxyRegistry.UnavailableCount()}/{ShellComProxyRegistry.Count}",
                $"logLinesLost={lostLogLines}",
                $"ghosts={Tasks.GhostsRemoved}",
                $"closedPanelsAlive={PanelLeakTracker.CollectAndReport()}",
            };

            return "Health: " + string.Join(", ", fields);
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Tick -= Timer_Tick;
            latencyMonitor.Dispose();
        }
    }
}
