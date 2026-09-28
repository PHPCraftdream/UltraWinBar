using System;
using System.Runtime.CompilerServices;
using UltraWinBar.Utilities;

// R9-J (round-8 review К19): the in-app panel leak detector (WeakReference-tracked closed
// Taskbars, reported after a forced GC pass at health-snapshot time) and the richer health line
// built on it - max UI-thread delay, CallbackGuard failures, ShellComProxy state, lost/dropped
// log lines, and the leak count.
internal static class HealthDiagnosticsChecks
{
    internal static void Run()
    {
        RunPanelLeakTrackerAliveCheck();
        RunPanelLeakTrackerCollectedCheck();
        RunPanelLeakTrackerBoundCheck();
        RunUiThreadLatencyMonitorCheck();
        RunHealthSnapshotFormatCheck();
    }

    // A strongly-held closed panel must still be reported as alive after the tracker's GC pass.
    private static void RunPanelLeakTrackerAliveCheck()
    {
        object stillOpenElsewhere = new object();
        const string label = "Bottom@HealthDiagnosticsChecks-alive";
        PanelLeakTracker.Track(stillOpenElsewhere, label);

        string report = PanelLeakTracker.CollectAndReport();
        if (!report.Contains(label))
            throw new Exception($"A strongly-held tracked panel must still be reported alive: {report}");
        GC.KeepAlive(stillOpenElsewhere);
        Console.WriteLine("PASS: PanelLeakTracker reports a strongly-held closed panel as still alive.");
    }

    // A released closed panel must disappear once the tracker's GC pass collects it.
    private static void RunPanelLeakTrackerCollectedCheck()
    {
        const string label = "Right@HealthDiagnosticsChecks-collected";
        TrackAndDrop(label);

        string report = PanelLeakTracker.CollectAndReport();
        if (report.Contains(label))
            throw new Exception($"A released tracked panel must be dropped once collected: {report}");
        Console.WriteLine("PASS: PanelLeakTracker drops a released closed panel once garbage-collected.");
    }

    // No inlining: keeps the only strong reference to the tracked object inside this frame, so it
    // is unreachable as soon as the frame returns, without the JIT extending its lifetime.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TrackAndDrop(string label)
    {
        PanelLeakTracker.Track(new object(), label);
    }

    // The list must stay bounded even under many closes (settings churn, repeated display events).
    private static void RunPanelLeakTrackerBoundCheck()
    {
        for (int i = 0; i < 50; i++) PanelLeakTracker.Track(new object(), $"Test@Bound{i}");
        if (PanelLeakTracker.TrackedCount > 32)
            throw new Exception($"PanelLeakTracker must bound its tracked list, got {PanelLeakTracker.TrackedCount}.");
        Console.WriteLine("PASS: PanelLeakTracker bounds its tracked list.");
    }

    // Max lateness and the >250ms/>1000ms threshold counts, driven entirely by injected
    // timestamps - no sleeps, no dependency on a live Dispatcher pump.
    private static void RunUiThreadLatencyMonitorCheck()
    {
        using var monitor = new UiThreadLatencyMonitor();
        monitor.ResetExpected(0);

        monitor.OnTick(0);     // expected 0: on time, lateness 0
        monitor.OnTick(1300);  // expected 1000: 300ms late -> warn (>250, not >1000)
        monitor.OnTick(3800);  // expected 2300: 1500ms late -> severe (>1000)

        var snapshot = monitor.ConsumeSnapshot();
        if (snapshot.MaxMs != 1500 || snapshot.WarnCount != 1 || snapshot.SevereCount != 1)
            throw new Exception($"Unexpected lateness accounting: max={snapshot.MaxMs}, warn={snapshot.WarnCount}, severe={snapshot.SevereCount}");

        var afterReset = monitor.ConsumeSnapshot();
        if (afterReset.MaxMs != 0 || afterReset.WarnCount != 0 || afterReset.SevereCount != 0)
            throw new Exception("ConsumeSnapshot must reset counters for the next health interval.");

        Console.WriteLine("PASS: UiThreadLatencyMonitor tracks max lateness and >250ms/>1000ms counts, and resets on read.");
    }

    // The health line must carry the new K19 fields alongside the existing resource snapshot.
    private static void RunHealthSnapshotFormatCheck()
    {
        using var health = new HealthReporter(null, null);
        string snapshot = health.Snapshot();
        foreach (var field in new[]
        {
            "uiMaxDelayMs=", "uiDelayOver250=", "uiDelayOver1000=",
            "callbackFailures=", "comProxiesDown=", "logLinesLost=", "closedPanelsAlive=",
        })
        {
            if (!snapshot.Contains(field))
                throw new Exception($"Health snapshot is missing {field}: {snapshot}");
        }
        Console.WriteLine("PASS: the health line reports UI-thread lateness, CallbackGuard failures, ShellComProxy state, lost log lines and leaked panels.");
    }
}
