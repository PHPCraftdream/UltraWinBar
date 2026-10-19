using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.Common.Native;
using ManagedShell.WindowsTray;
using UltraWinBar.Utilities;

// Startup: the stage timeline, the run-once gate that announces the tray, and the order in which Explorer's taskbar is hidden.
internal static class StartupChecks
{
    internal static void Run()
    {
        RunTimelineCheck();
        RunOneShotCheck();
        RunHideOrderCheck();
        RunSelfAnnouncementCheck();
    }

    // Regression: the deferred tray announcement is delivered to ExplorerMonitor, which took it for Explorer
    // restarting and closed and reopened every panel ~1.5 s after start (the panels flashed).
    private static void RunSelfAnnouncementCheck()
    {
        const int taskbarCreated = 0xC0DE;
        if (!ExplorerMonitor.IsExplorerRestart(taskbarCreated, taskbarCreated))
            throw new Exception("TaskbarCreated from outside (Explorer restarting) must rebuild the panels.");
        if (ExplorerMonitor.IsExplorerRestart(taskbarCreated + 1, taskbarCreated))
            throw new Exception("Other messages are not an Explorer restart.");

        using (ExplorerLifecycle.BeginSelfAnnouncement())
        {
            if (ExplorerMonitor.IsExplorerRestart(taskbarCreated, taskbarCreated))
                throw new Exception("Our own TaskbarCreated announcement must not rebuild the panels.");
            using (ExplorerLifecycle.BeginSelfAnnouncement()) { }
            if (!ExplorerLifecycle.IsSelfAnnouncing)
                throw new Exception("Ending a nested announcement must not end the outer one.");
        }
        if (ExplorerLifecycle.IsSelfAnnouncing)
            throw new Exception("The suppression must end with the announcement.");

        try
        {
            using (ExplorerLifecycle.BeginSelfAnnouncement()) throw new InvalidOperationException();
        }
        catch (InvalidOperationException) { }
        if (ExplorerLifecycle.IsSelfAnnouncing || !ExplorerMonitor.IsExplorerRestart(taskbarCreated, taskbarCreated))
            throw new Exception("A failing broadcast must not leave a real Explorer restart ignored.");
        Console.WriteLine("PASS: ExplorerMonitor ignores the TaskbarCreated we broadcast ourselves (no panel reopening at start) and still reacts to a real Explorer restart.");
    }

    private static void RunTimelineCheck()
    {
        long now = 1200;
        var lines = new List<string>();
        var timeline = new StartupTimeline(() => now, lines.Add);

        timeline.Record("Main entered");
        now = 1450;
        timeline.Record("Shell services started");
        if (lines.Count != 0) throw new Exception("Marks made before the log opens must be held, not written.");

        timeline.Open();
        string[] held =
        {
            "Startup: Main entered at 1200 ms (+1200 ms)",
            "Startup: Shell services started at 1450 ms (+250 ms)"
        };
        if (!lines.SequenceEqual(held))
            throw new Exception("Held marks must be written in order with the time since the previous mark: " + string.Join(" | ", lines));

        now = 1500;
        timeline.Record("OnStartup entered");
        timeline.Open();
        if (lines.Count != 3 || lines[2] != "Startup: OnStartup entered at 1500 ms (+50 ms)")
            throw new Exception("After the log opens marks are written at once, and opening again repeats nothing: " + string.Join(" | ", lines));
        Console.WriteLine("PASS: the startup timeline holds marks made before the logger exists, writes them in order with per-stage deltas, and writes later marks at once.");
    }

    private static void RunOneShotCheck()
    {
        int runs = 0;
        var gate = new OneShot(() => runs++);
        if (!gate.Fire() || gate.Fire() || runs != 1)
            throw new Exception("The first trigger must run the action and a second trigger (timeout after completion) must not.");

        runs = 0;
        var cancelled = new OneShot(() => runs++);
        cancelled.Cancel();
        if (cancelled.Fire() || runs != 0)
            throw new Exception("A cancelled gate (the tray was disposed while Explorer was still being read) must never announce the tray.");
        Console.WriteLine("PASS: the tray is announced exactly once - by the first of the icon read finishing or its timeout - and never after dispose.");
    }

    private static void RunHideOrderCheck()
    {
        if (WindowManager.HidesExplorerTaskbarBeforePanels(true))
            throw new Exception("Manual-work-area panels are placed absolutely: they must be shown before Explorer's taskbar goes, or the screen is bare.");
        if (!WindowManager.HidesExplorerTaskbarBeforePanels(false))
            throw new Exception("AppBar-registered panels are laid out around Explorer's own: it must be hidden first.");
        Console.WriteLine("PASS: Explorer's taskbar is hidden after the panels in manual work-area mode and before them when panels register as AppBars.");
    }
}
