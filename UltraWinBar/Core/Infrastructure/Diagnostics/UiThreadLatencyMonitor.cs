using System;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    // K19: a Background-priority DispatcherTimer measuring its own lateness is a cheap proxy for
    // UI-thread hangs (Н4) - no per-frame work, one tick a second. Keeps only the max lateness and
    // threshold counts over the health interval; HealthReporter reads and resets them.
    internal sealed class UiThreadLatencyMonitor : IDisposable
    {
        internal const long WarnThresholdMs = 250;
        internal const long SevereThresholdMs = 1000;
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        private readonly DispatcherTimer timer;
        private long expectedTickMs;
        private long maxLatenessMs;
        private long warnCount;
        private long severeCount;

        internal UiThreadLatencyMonitor()
        {
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
            timer.Tick += (_, _) => OnTick(Environment.TickCount64);
            expectedTickMs = Environment.TickCount64 + (long)TickInterval.TotalMilliseconds;
            timer.Start();
        }

        // Feeds one tick's actual fire time through the same accounting the real timer uses.
        // Exposed so a test can drive it with injected timestamps instead of real sleeps.
        internal void OnTick(long actualTickMs)
        {
            long lateness = actualTickMs - expectedTickMs;
            if (lateness > maxLatenessMs) maxLatenessMs = lateness;
            if (lateness > SevereThresholdMs) severeCount++;
            else if (lateness > WarnThresholdMs) warnCount++;
            expectedTickMs = actualTickMs + (long)TickInterval.TotalMilliseconds;
        }

        // Test hook: pins the next expected tick to a known value instead of real wall time.
        internal void ResetExpected(long expectedMs) => expectedTickMs = expectedMs;

        // Reads current counters and resets them for the next health interval.
        internal (long MaxMs, long WarnCount, long SevereCount) ConsumeSnapshot()
        {
            var snapshot = (maxLatenessMs, warnCount, severeCount);
            maxLatenessMs = 0;
            warnCount = 0;
            severeCount = 0;
            return snapshot;
        }

        public void Dispose()
        {
            timer.Stop();
        }
    }
}
