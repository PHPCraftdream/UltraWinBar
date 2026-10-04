using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class LayoutChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        var widthConverter = new UltraWinBar.Converters.TaskButtonWidthConverter();
        foreach (bool launcher in new[] { false, true })
        foreach (var orientation in new[] { System.Windows.Controls.Orientation.Horizontal, System.Windows.Controls.Orientation.Vertical })
        {
            double expectedWidth = launcher && orientation == System.Windows.Controls.Orientation.Horizontal ? 34 : 132;
            object actualWidth = widthConverter.Convert(new object[] { 0, 132d, 0, 1, launcher, orientation },
                typeof(double), null, System.Globalization.CultureInfo.InvariantCulture);
            if (!expectedWidth.Equals(actualWidth)) throw new Exception($"Pinned width mismatch: {launcher}/{orientation}");
        }
        Console.WriteLine("PASS: vertical pinned launchers fill the panel; horizontal launchers remain compact.");
        var screen = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var edges = new[] { AppBarEdge.Left, AppBarEdge.Top, AppBarEdge.Right, AppBarEdge.Bottom };
        void VerifyNonOverlapping(AppBarEdge[] order, ManagedShell.Interop.NativeMethods.Rect bounds, int thickness)
        {
            var layout = PanelLayout.Calculate(bounds, order, _ => thickness);
            foreach (var (_, panel) in layout.Panels)
            {
                if (panel.Left < bounds.Left || panel.Right > bounds.Right || panel.Top < bounds.Top || panel.Bottom > bounds.Bottom ||
                    panel.Width < 0 || panel.Height < 0)
                    throw new Exception("Panel exceeds the monitor bounds.");
            }
            for (int i = 0; i < layout.Panels.Count; i++)
            for (int j = i + 1; j < layout.Panels.Count; j++)
            {
                var panel = layout.Panels[i].Bounds;
                var other = layout.Panels[j].Bounds;
                if (Math.Min(panel.Right, other.Right) > Math.Max(panel.Left, other.Left) &&
                    Math.Min(panel.Bottom, other.Bottom) > Math.Max(panel.Top, other.Top))
                    throw new Exception($"Panels overlap: {layout.Panels[i].Edge} and {layout.Panels[j].Edge}.");
            }
        }
        void CheckPermutations(AppBarEdge[] chosen, AppBarEdge[] remaining)
        {
            VerifyNonOverlapping(chosen, screen, 158);
            VerifyNonOverlapping(chosen, new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 220, Bottom = 180 }, 158);
            foreach (var edge in remaining)
                CheckPermutations(chosen.Append(edge).ToArray(), remaining.Where(candidate => candidate != edge).ToArray());
        }
        CheckPermutations(Array.Empty<AppBarEdge>(), edges);
        var topFirst = PanelLayout.Calculate(screen, new[] { AppBarEdge.Top, AppBarEdge.Left }, _ => 40);
        if (topFirst.Panels[0].Bounds.Right != 1920 || topFirst.Panels[1].Bounds.Top != 40)
            throw new Exception("Edge priority was not preserved.");
        Console.WriteLine("PASS: panel bounds never intersect for every edge subset/order, including tight monitors.");
        var placementGuard = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.WindowPlacementGuard");
        var planPlacement = placementGuard?.GetMethod("PlanPlacement", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (planPlacement == null) throw new Exception("Window placement policy is missing.");
        System.Windows.Rect? PlanWindow(ManagedShell.Interop.NativeMethods.Rect visible, System.Windows.Rect area, bool maximized) =>
            (System.Windows.Rect?)planPlacement.Invoke(null, new object[] { visible, area, maximized });
        var reservedArea = new System.Windows.Rect(158, 41, 1604, 998);
        var oversizedWindow = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        if (PlanWindow(oversizedWindow, reservedArea, true).HasValue)
            throw new Exception("Maximized windows must retain Windows-managed placement and restore bounds.");
        var normalWindow = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 900, Bottom = 700 };
        var relocated = PlanWindow(normalWindow, reservedArea, false);
        if (!relocated.HasValue || relocated.Value.X != 158 || relocated.Value.Y != 41 ||
            relocated.Value.Width != 900 || relocated.Value.Height != 700 ||
            PlanWindow(new ManagedShell.Interop.NativeMethods.Rect { Left = 158, Top = 41, Right = 1058, Bottom = 741 },
                reservedArea, false).HasValue)
            throw new Exception("Restored windows must move out of panel space without changing a fitting size.");
        var clipped = PlanWindow(oversizedWindow, reservedArea, false);
        if (!clipped.HasValue || clipped.Value != reservedArea)
            throw new Exception("An oversized restored window must fit the reserved work area.");
        Console.WriteLine("PASS: maximized placement is untouched; restored windows move without unnecessary resizing.");
        var planRefit = placementGuard.GetMethod("PlanMaximizedRefit", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (planRefit == null) throw new Exception("Maximized refit policy is missing.");
        ManagedShell.Interop.NativeMethods.Rect? PlanRefit(ManagedShell.Interop.NativeMethods.Rect outer, ManagedShell.Interop.NativeMethods.Rect monitor, System.Windows.Rect area) =>
            (ManagedShell.Interop.NativeMethods.Rect?)planRefit.Invoke(null, new object[] { outer, monitor, area });
        ManagedShell.Interop.NativeMethods.Rect R(int l, int t, int r, int b) => new ManagedShell.Interop.NativeMethods.Rect { Left = l, Top = t, Right = r, Bottom = b };
        var monitorRect = R(0, 0, 1920, 1080);
        // Regression: after unlock Windows maximized into the cleared full-monitor work area (8px invisible border).
        var refit = PlanRefit(R(-8, -8, 1928, 1088), monitorRect, reservedArea);
        if (!refit.HasValue || !refit.Value.Equals(R(150, 33, 1770, 1047)))
            throw new Exception($"A maximized window stretched over the panels must refit the panel work area keeping its border, got {refit}.");
        if (PlanRefit(R(0, 0, 1920, 1080), monitorRect, reservedArea) is not { } borderless || !borderless.Equals(R(158, 41, 1762, 1039)))
            throw new Exception("A borderless maximized window must refit exactly the panel work area.");
        if (PlanRefit(R(150, 33, 1770, 1047), monitorRect, reservedArea).HasValue)
            throw new Exception("A maximized window already inside the panel work area must not move.");
        if (PlanRefit(R(-8, -8, 1928, 1088), monitorRect, new System.Windows.Rect(0, 0, 1920, 1080)).HasValue)
            throw new Exception("Without reserved panel space (auto-hide) a full-monitor maximized window must stay.");
        if (PlanRefit(R(-200, -8, 1928, 1088), monitorRect, reservedArea).HasValue)
            throw new Exception("A window spanning beyond its monitor is not a maximized-into-monitor window.");
        Console.WriteLine("PASS: maximized windows stretched over panels by a cleared work area refit the panel work area once.");
        var workAreaManager = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.WorkAreaManager");
        var isCurrentWorkArea = workAreaManager?.GetMethod("IsCurrent");
        var liveWorkArea = new ManagedShell.Interop.NativeMethods.Rect();
        if (isCurrentWorkArea == null ||
            !ManagedShell.Interop.NativeMethods.SystemParametersInfo(
                (int)ManagedShell.Interop.NativeMethods.SPI.GETWORKAREA, 0, ref liveWorkArea, 0))
            throw new Exception("Cannot read the current system work area.");
        bool IsCurrentWorkArea(ManagedShell.Interop.NativeMethods.Rect expected) =>
            (bool)isCurrentWorkArea.Invoke(null, new object[] { expected });
        if (!IsCurrentWorkArea(liveWorkArea)) throw new Exception("The current system work area was not recognized.");
        liveWorkArea.Left++;
        if (IsCurrentWorkArea(liveWorkArea)) throw new Exception("A lost work area was not detected.");
        Console.WriteLine("PASS: system work-area reconciliation detects a changed rectangle without writing system state.");
        var recoveryType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.WorkAreaRecovery");
        var recoverMethod = recoveryType?.GetMethod("Recover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (recoverMethod == null) throw new Exception("Work-area recovery controller is missing.");
        object NewRecovery() => Activator.CreateInstance(recoveryType, true);
        string RecoverArea(object controller, long time, Func<bool> current, Action apply) =>
            recoverMethod.Invoke(controller, new object[] { time, current, apply }).ToString();
        var recovery = NewRecovery();
        int workAreaWrites = 0;
        Action writeWorkArea = () =>
        {
            workAreaWrites++;
            if (RecoverArea(recovery, 0, () => false, () => workAreaWrites++) != "Deferred")
                throw new Exception("A reentrant work-area notification was not suppressed.");
        };
        if (RecoverArea(recovery, 0, () => true, writeWorkArea) != "Unchanged" || workAreaWrites != 0 ||
            RecoverArea(recovery, 0, () => false, writeWorkArea) != "Applied")
            throw new Exception("Work-area recovery wrote an unchanged area or missed the initial reset.");
        long RetryAt(object controller) => (long)recoveryType.GetProperty("RetryAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
        long SuspendedFor(object controller) => (long)recoveryType.GetProperty("SuspendedFor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
        void ResetRecovery(object controller) => recoveryType.GetMethod("Reset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
        foreach (long time in new long[] { 1, 100, 500, 1999 })
            if (RecoverArea(recovery, time, () => false, writeWorkArea) != "Deferred" || RetryAt(recovery) != 2000)
                throw new Exception("A burst of work-area notifications bypassed the cooldown or lost its retry time.");
        if (RecoverArea(recovery, 2000, () => false, writeWorkArea) != "Applied" ||
            RecoverArea(recovery, 4000, () => false, writeWorkArea) != "Applied" ||
            RecoverArea(recovery, 6000, () => false, writeWorkArea) != "Suspended" || workAreaWrites != 3 ||
            SuspendedFor(recovery) != 60000 || RetryAt(recovery) != 66000 ||
            RecoverArea(recovery, 30000, () => throw new Exception("Suspended recovery still read OS state."), writeWorkArea) != "Deferred" ||
            RetryAt(recovery) != 66000)
            throw new Exception("Competing work-area changes can cause unbounded desktop resizing.");
        if (RecoverArea(recovery, 66000, () => false, writeWorkArea) != "Applied" || workAreaWrites != 4 ||
            RecoverArea(recovery, 68000, () => false, writeWorkArea) != "Suspended" || SuspendedFor(recovery) != 300000 ||
            RecoverArea(recovery, 368000, () => false, writeWorkArea) != "Applied" ||
            RecoverArea(recovery, 370000, () => false, writeWorkArea) != "Suspended" || SuspendedFor(recovery) != 900000 || workAreaWrites != 5)
            throw new Exception("A persistent work-area conflict did not back off with a single probe per pause.");
        if (RecoverArea(recovery, 1270000, () => false, writeWorkArea) != "Applied" ||
            RecoverArea(recovery, 1330000, () => false, writeWorkArea) != "Applied" ||
            RecoverArea(recovery, 1332000, () => false, writeWorkArea) != "Applied" ||
            RecoverArea(recovery, 1334000, () => false, writeWorkArea) != "Applied" ||
            RecoverArea(recovery, 1336000, () => false, writeWorkArea) != "Suspended" || SuspendedFor(recovery) != 60000)
            throw new Exception("A probe that held for the quiet period did not restore the full recovery budget.");
        ResetRecovery(recovery);
        if (RecoverArea(recovery, 1337000, () => false, writeWorkArea) != "Applied" || workAreaWrites != 10)
            throw new Exception("Reopening the panels did not end a work-area recovery pause.");
        var delayedRecovery = NewRecovery();
        foreach (long time in new long[] { 0, 2000, 4000, 64000 })
            if (RecoverArea(delayedRecovery, time, () => false, () => { }) != "Applied")
                throw new Exception("Isolated work-area resets incorrectly exhausted the recovery budget.");
        var failedRecovery = NewRecovery();
        try
        {
            RecoverArea(failedRecovery, 0, () => false, () => throw new InvalidOperationException("Rejected work-area write."));
            throw new Exception("Expected the injected work-area write to fail.");
        }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
        if (RecoverArea(failedRecovery, 2000, () => false, () => { }) != "Applied")
            throw new Exception("A failed write permanently held the recovery reentrancy guard.");
        Console.WriteLine("PASS: repeated and reentrant work-area resets are bounded; conflicts pause writes with escalating backoff and retry after each pause.");
    }
}
