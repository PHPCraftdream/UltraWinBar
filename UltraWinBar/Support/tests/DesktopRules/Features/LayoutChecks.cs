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
        RunResizeChecks(repositoryRoot);
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
        var placementGuard = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.WindowPlacementGuard")
            ?? throw new Exception("Window placement guard is missing.");
        // Regression: the guard moved and shrank every restored window into the panel work area after it was shown,
        // activated or dragged, so windows could not be dragged past the panels or off the work area.
        // Only maximized windows are fitted to the panels; a restored window is never resized, and is moved only when
        // its title bar is out of the mouse's reach (see the PlanReach checks below).
        var guardMembers = placementGuard.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Select(member => member.Name).ToArray();
        if (guardMembers.Contains("PlanPlacement") || guardMembers.Contains("Constrain"))
            throw new Exception("The placement guard must not relocate or resize restored windows.");
        string guardSource = System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot.FullName,
            "UltraWinBar", "Core", "Desktop", "WindowPlacementGuard.cs"));
        if (System.Text.RegularExpressions.Regex.Matches(guardSource, @"\bSetWindowPos\(").Count != 2 ||
            guardSource.IndexOf("SetWindowPos(") > guardSource.IndexOf("private void RefitMaximizedWindows") ||
            !guardSource.Substring(guardSource.IndexOf("internal static bool BringIntoReach"), guardSource.IndexOf("private static bool IsMovable") -
                guardSource.IndexOf("internal static bool BringIntoReach")).Contains("SWP_NOSIZE"))
            throw new Exception("The placement guard may reposition a window in two places only: the maximized refit and the size-keeping title bar rescue.");
        if (guardSource.Contains("WinEventHook(") || guardSource.Contains("0x000A") || guardSource.Contains("0x000B"))
            throw new Exception("The placement guard must not watch window dragging: a window is dragged freely while its title bar can be grabbed.");
        Console.WriteLine("PASS: the placement guard never resizes a restored window nor watches dragging; it repositions only maximized windows and those with an unreachable title bar.");

        // Regression: after a logon two of the four panels (left and bottom) were not shown at all while the other two
        // were, until the desktops were switched; nothing was logged. The guard names what is wrong with a panel and
        // escalates from putting it right to opening the panels again, but never fights a panel that conceded to a
        // full-screen app.
        var healthyPanel = new PanelVisibilityGuard.PanelState { Visible = true, Opacity = 1, ExpectOnTop = true, Topmost = true, Reachable = true };
        string ProblemsOf(Func<PanelVisibilityGuard.PanelState, PanelVisibilityGuard.PanelState> change) => PanelVisibilityGuard.Problems(change(healthyPanel));
        if (PanelVisibilityGuard.Problems(healthyPanel).Length != 0)
            throw new Exception("A panel that is shown, opaque, topmost and reachable has no problems.");
        if (ProblemsOf(state => { state.Visible = false; state.Reachable = false; return state; }) != "not visible")
            throw new Exception("A panel that is not visible must be reported as such, and nothing else.");
        if (ProblemsOf(state => { state.Cloaked = 2; state.Reachable = false; return state; }) != "cloaked (2)")
            throw new Exception("A cloaked panel must be reported with its cloak reason, and nothing else.");
        if (ProblemsOf(state => { state.Opacity = 0; return state; }) != "opacity 0")
            throw new Exception("A fully transparent panel must be reported.");
        if (ProblemsOf(state => { state.Topmost = false; return state; }) != "not topmost")
            throw new Exception("A panel that lost its topmost style must be reported.");
        if (ProblemsOf(state => { state.Reachable = false; return state; }) != "covered")
            throw new Exception("A panel with another window over every probed point must be reported as covered.");
        if (ProblemsOf(state => { state.ExpectOnTop = false; state.Topmost = false; state.Reachable = false; return state; }).Length != 0)
            throw new Exception("A panel that conceded to a full-screen app belongs at the bottom and must be left alone.");
        if (PanelVisibilityGuard.ActionFor(1, 0) != "reassert" || PanelVisibilityGuard.ActionFor(2, 0) != "reopen" ||
            PanelVisibilityGuard.ActionFor(2, 1) != "report" || PanelVisibilityGuard.ActionFor(3, 0) != "report")
            throw new Exception("A panel must be put right first, then the panels opened again once, then only reported.");
        if (PanelVisibilityGuard.Schedule[0] > TimeSpan.FromSeconds(5) ||
            PanelVisibilityGuard.Schedule.Aggregate(TimeSpan.Zero, (total, delay) => total + delay) < TimeSpan.FromMinutes(2))
            throw new Exception("The panel checks must begin within seconds of start-up and last past the logon rush.");
        Console.WriteLine("PASS: the panel visibility guard names what is wrong with a panel, escalates reassert -> reopen -> report, and leaves a panel that conceded to a full-screen app alone.");
        var reservedArea = new System.Windows.Rect(158, 41, 1604, 998);
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
        // Regression: after a panel resize maximized windows kept the previous work area (Windows does not re-maximize).
        var widerRight = new System.Windows.Rect(158, 41, 1690 - 158, 1039 - 41);
        if (PlanRefit(R(150, 33, 1770, 1047), R(158, 41, 1762, 1039), widerRight) is not { } followed || !followed.Equals(R(150, 33, 1698, 1047)))
            throw new Exception("A window maximized into the previous work area must follow a panel resize keeping its border.");
        if (PlanRefit(R(150, 33, 1698, 1047), R(158, 41, 1690, 1039), widerRight).HasValue)
            throw new Exception("A window already fitted to the new work area must not move.");
        if (PlanRefit(R(300, 200, 900, 700), R(158, 41, 1762, 1039), widerRight).HasValue)
            throw new Exception("A window not maximized into the previous work area must not be refitted.");
        Console.WriteLine("PASS: maximized windows follow a panel resize from the previous work area to the new one.");
        // Regression: a dialog opened at (0,0) had its title bar under the top panel and could not be dragged at all;
        // a window parked at (-32000,-32000) could not even be seen. A window is moved into the work area only when
        // no draggable part of its title bar (not the icon, not the caption buttons) is within the mouse's reach.
        var planReach = placementGuard.GetMethod("PlanReach", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("Title bar reach policy is missing.");
        var reachScreen = new System.Windows.Rect(0, 0, 1920, 1080);
        ManagedShell.Interop.NativeMethods.Rect? PlanReach(ManagedShell.Interop.NativeMethods.Rect window, bool activated, params System.Windows.Rect[] areas) =>
            (ManagedShell.Interop.NativeMethods.Rect?)planReach.Invoke(null, new object[] { window, 31, 31, 138, areas, areas[0], reachScreen, activated });
        void ExpectReach(string what, ManagedShell.Interop.NativeMethods.Rect window, ManagedShell.Interop.NativeMethods.Rect? expected, params System.Windows.Rect[] areas) =>
            ExpectReachWhen(what, window, true, expected, areas);
        void ExpectReachWhen(string what, ManagedShell.Interop.NativeMethods.Rect window, bool activated, ManagedShell.Interop.NativeMethods.Rect? expected, params System.Windows.Rect[] areas)
        {
            var actual = PlanReach(window, activated, areas);
            if (actual.HasValue != expected.HasValue || actual.HasValue && !actual.Value.Equals(expected.Value))
                throw new Exception($"{what}: expected {(expected.HasValue ? expected.ToString() : "no move")}, got {(actual.HasValue ? actual.ToString() : "no move")}.");
            if (actual.HasValue && (actual.Value.Width != window.Width || actual.Value.Height != window.Height))
                throw new Exception($"{what}: the window size must be kept.");
            if (actual.HasValue && PlanReach(actual.Value, true, areas).HasValue)
                throw new Exception($"{what}: the moved window must be reachable, so a second pass must not move it again.");
        }
        ExpectReach("A dialog at (0,0) with its title bar under the top panel", R(0, 0, 1414, 910), R(158, 41, 1572, 951), reservedArea);
        ExpectReach("A window parked at (-32000,-32000)", R(-32000, -32000, -31700, -31750), R(810, 415, 1110, 665), reservedArea);
        ExpectReach("A window whose title bar is reachable", R(400, 300, 1000, 800), null, reservedArea);
        ExpectReach("A title bar with 20 px of its height below the top panel", R(300, 30, 900, 600), null, reservedArea);
        ExpectReach("A title bar with only 8 px of its height below the top panel", R(300, 18, 900, 600), R(300, 41, 900, 623), reservedArea);
        ExpectReach("A window partly under the left panel with a reachable title bar", R(0, 200, 800, 600), null, reservedArea);
        ExpectReach("A title bar with 81 px of its draggable part in reach", R(1650, 300, 2250, 700), null, reservedArea);
        ExpectReach("A title bar with 31 px of its draggable part in reach", R(1700, 300, 2300, 700), R(1162, 300, 1762, 700), reservedArea);
        ExpectReach("A window of which only the caption buttons are in reach", R(-300, 300, 300, 700), R(158, 300, 758, 700), reservedArea);
        ExpectReach("A non-maximized window larger than the work area", R(100, 0, 1800, 1050), R(158, 41, 1858, 1091), reservedArea);
        ExpectReach("A full-screen window that keeps its caption style", R(0, 0, 1920, 1080), null, reservedArea);
        ExpectReach("A full-screen window with a frame overhang", R(-8, -8, 1928, 1088), null, reservedArea);
        ExpectReach("A title bar reachable on the second monitor", R(2000, 100, 2600, 500), null, reservedArea, new System.Windows.Rect(1920, 0, 1920, 1080));
        ExpectReach("A window without size", R(100, 100, 100, 100), null, reservedArea);
        ExpectReachWhen("A window only shown (not activated) off every monitor, maybe parked there on purpose",
            R(-32000, -32000, -31700, -31750), false, null, reservedArea);
        ExpectReachWhen("A window only shown at (0,0) with its title bar under the top panel", R(0, 0, 1414, 910), false, R(158, 41, 1572, 951), reservedArea);
        Console.WriteLine("PASS: a window moves into the work area only when no draggable part of its title bar is within the mouse's reach; its size is kept; full-screen windows, and windows merely shown off-screen, stay.");
        // Regression: Telegram's media viewer draws its own title bar (no WS_CAPTION); opened with it under the top panel
        // it was left there. An application's window without a system title bar is rescued too; tool windows, windows
        // that cannot be activated, owned popups and child windows are not.
        var hasTitleStrip = placementGuard.GetMethod("HasTitleStrip", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("The title strip rule is missing.");
        bool Strip(int style, int exStyle, bool owned) => (bool)hasTitleStrip.Invoke(null, new object[] { style, exStyle, owned });
        if (!Strip(unchecked((int)0x16CF0000), 0, false) || !Strip(unchecked((int)0x96000000), 0, false) || !Strip(unchecked((int)0x96000000), 0x00040000, true) ||
            !Strip(unchecked((int)0x16CF0000), 0, true) || Strip(unchecked((int)0x96000000), 0, true) || Strip(unchecked((int)0x96000000), 0x00000080, false) ||
            Strip(unchecked((int)0x96000000), 0x08000000, false) || Strip(0x56000000, 0, false) || Strip(unchecked((int)0x16CF0000), 0x00000020, false))
            throw new Exception("Windows with a title bar of their own must be rescued like those with the system's, popups and tool windows not.");
        if (!guardSource.Contains("outer.Width >= MinFramelessWidth && outer.Height >= MinFramelessHeight") ||
            System.Text.RegularExpressions.Regex.Matches(guardSource, @"HasTitleStrip\(").Count != 3)
            throw new Exception("Both the watch and the rescue must use the title strip rule, and a small window without a title bar is left alone.");
        Console.WriteLine("PASS: windows that draw their own title bar are brought into reach like those with the system's; popups, tool windows and small windows stay.");
        // Regression: Telegram's media viewer is (0,0)-(1921,1080) on a 1920x1080 monitor; it was not taken for a full-screen
        // window, so the panels stayed over it and hid its top. A window covering a monitor a few pixels past its edges is.
        var monitor1080 = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        if (!FullScreenHelper.CoversScreen(R(0, 0, 1921, 1080), monitor1080, FullScreenHelper.FullScreenOverhang) ||
            !FullScreenHelper.CoversScreen(R(0, 0, 1920, 1080), monitor1080, 0) || FullScreenHelper.CoversScreen(R(0, 0, 1921, 1080), monitor1080, 0) ||
            FullScreenHelper.CoversScreen(R(0, 40, 1920, 1080), monitor1080, FullScreenHelper.FullScreenOverhang) ||
            FullScreenHelper.CoversScreen(R(-100, 0, 1920, 1080), monitor1080, FullScreenHelper.FullScreenOverhang) ||
            FullScreenHelper.CoversScreen(R(0, 0, 3840, 1080), monitor1080, FullScreenHelper.FullScreenOverhang))
            throw new Exception("A window a few pixels larger than a monitor is full screen on it; one short of it, or spanning far past it, is not.");
        Console.WriteLine("PASS: a window covering a monitor up to a few pixels past its edges (Telegram's media viewer) is full screen.");
        // Regression: Explorer recomputes the work area on unlock from registered AppBars only; manual
        // panels must publish their exact rect on their own callback, not ManagedShell's AppBar message.
        var reservation = PanelReservation.Build((IntPtr)0x1234, AppBarEdge.Left, R(0, 41, 158, 1080));
        if (reservation.hWnd != (IntPtr)0x1234 || reservation.uEdge != 0 || !reservation.rc.Equals(R(0, 41, 158, 1080)) ||
            reservation.cbSize != System.Runtime.InteropServices.Marshal.SizeOf<ManagedShell.Interop.NativeMethods.APPBARDATA>() ||
            reservation.uCallbackMessage != PanelReservation.CallbackMessage || PanelReservation.CallbackMessage == 0 ||
            PanelReservation.CallbackMessage == ManagedShell.Interop.NativeMethods.RegisterWindowMessage("AppBarMessage"))
            throw new Exception("Panel reservation must carry the panel's exact edge rect and a dedicated callback message.");
        if (PanelReservation.Build(IntPtr.Zero, AppBarEdge.Bottom, R(0, 1039, 1920, 1080)).uEdge != 3)
            throw new Exception("Panel reservation edge must match the shell ABE_* values.");
        Console.WriteLine("PASS: manual panels reserve their exact rects with the shell under a dedicated callback message.");
        // The work area of the last run is remembered with its screen, and read back only when it is sane.
        var rememberedScreen = R(0, 0, 1920, 1080);
        string remembered = WorkAreaMemory.Format(R(148, 34, 1772, 1046), rememberedScreen);
        if (remembered != "148,34,1772,1046;0,0,1920,1080" || !WorkAreaMemory.TryParse(remembered, out var rememberedArea, out var rememberedOn) ||
            !rememberedArea.Equals(R(148, 34, 1772, 1046)) || !rememberedOn.Equals(rememberedScreen))
            throw new Exception("The remembered work area does not make the round trip: " + remembered);
        foreach (string bad in new[] { null, "", "148,34,1772,1046", "1,2,3;0,0,1920,1080", "a,b,c,d;0,0,1920,1080", "0,0,0,0;0,0,1920,1080",
            "-10,0,100,100;0,0,1920,1080", "0,0,1921,1080;0,0,1920,1080", "100,100,50,50;0,0,1920,1080", "0,0,100,100;0,0,0,0", "1,2,3,4;5,6,7,8;9" })
            if (WorkAreaMemory.TryParse(bad, out _, out _)) throw new Exception("A work area that cannot be right was accepted: " + bad);
        Console.WriteLine("PASS: the work area of the last run is remembered with its screen and read back only when it is non-empty and within that screen.");
        // After a restart that kept the panels' work area the original is the remembered one; in every other case the live one.
        var live = R(148, 34, 1772, 1046);
        string layoutText = WorkAreaMemory.Format(live, rememberedScreen), originalText = WorkAreaMemory.Format(R(0, 0, 1920, 1040), rememberedScreen);
        if (!WorkAreaMemory.ResolveOriginal(live, rememberedScreen, layoutText, originalText).Equals(R(0, 0, 1920, 1040)))
            throw new Exception("After a kept work area the original is the remembered one.");
        if (!WorkAreaMemory.ResolveOriginal(R(0, 0, 1920, 1040), rememberedScreen, layoutText, originalText).Equals(R(0, 0, 1920, 1040)) ||
            !WorkAreaMemory.ResolveOriginal(live, rememberedScreen, "", originalText).Equals(live) ||
            !WorkAreaMemory.ResolveOriginal(live, rememberedScreen, layoutText, "").Equals(live) ||
            !WorkAreaMemory.ResolveOriginal(live, R(0, 0, 2560, 1440), layoutText, originalText).Equals(live) ||
            !WorkAreaMemory.ResolveOriginal(live, rememberedScreen, layoutText, layoutText).Equals(live))
            throw new Exception("Without a kept work area, or on another screen, the live work area is the original.");
        string markerFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-test-restarting-" + Guid.NewGuid().ToString("N"));
        RestartMarker.PathOverride = markerFile;
        try
        {
            if (RestartMarker.IsFresh()) throw new Exception("No mark, no restart.");
            RestartMarker.Create();
            if (!RestartMarker.IsFresh()) throw new Exception("A mark just made is fresh.");
            System.IO.File.SetLastWriteTimeUtc(markerFile, DateTime.UtcNow.AddMinutes(-3));
            if (RestartMarker.IsFresh()) throw new Exception("A mark older than two minutes means nothing: a crash must not be taken for a restart.");
            RestartMarker.Create();
            RestartMarker.Clear();
            if (RestartMarker.IsFresh() || System.IO.File.Exists(markerFile)) throw new Exception("A cleared mark is gone.");
        }
        finally
        {
            RestartMarker.PathOverride = null;
            System.IO.File.Delete(markerFile);
        }
        string sources = System.IO.Path.Combine(repositoryRoot.FullName, "UltraWinBar");
        string program = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Shell", "Program.cs"));
        int watchdog = program.IndexOf("private static int RunWorkAreaWatchdog", StringComparison.Ordinal);
        if (watchdog < 0 || program.IndexOf("RestartMarker.IsFresh()", watchdog, StringComparison.Ordinal) < 0 ||
            program.IndexOf("RestartMarker.IsFresh()", watchdog, StringComparison.Ordinal) > program.IndexOf("WorkAreaManager.Apply(", watchdog, StringComparison.Ordinal))
            throw new Exception("The watchdog must look for the restart mark before it gives the work area back.");
        string windowManager = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Core", "Desktop", "WindowManager.cs"));
        string app = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Shell", "App.xaml.cs"));
        string script = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Support", "BuildTools", "scripts", "Reinstall-UltraWinBar.ps1"));
        int restartApp = app.IndexOf("public void RestartApp()", StringComparison.Ordinal);
        if (!windowManager.Contains("_manualWorkArea && !RestartMarker.IsFresh()") || !windowManager.Contains("RestartMarker.Clear();") ||
            restartApp < 0 || app.IndexOf("RestartMarker.Create();", restartApp, StringComparison.Ordinal) < 0 ||
            app.IndexOf("RestartMarker.Clear();", restartApp, StringComparison.Ordinal) < 0 ||
            script.IndexOf("'restarting'", StringComparison.Ordinal) < 0 || script.IndexOf("'restarting'", StringComparison.Ordinal) > script.IndexOf("Stop-Process", StringComparison.Ordinal))
            throw new Exception("A restart on purpose must leave the work area, and the next start must clear the mark.");
        Console.WriteLine("PASS: a restart on purpose (the Restart menu, the deploy script) leaves the work area in place and the next start takes it over with the remembered original; a crash, with no fresh mark, gives the work area back at once.");
        // Shown panels of a manual layout start at their final rect: Show used to place each across its whole side first.
        string appBarWindow = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Support", "vendor", "ManagedShell", "src", "ManagedShell.AppBar", "AppBarWindow.cs"));
        string taskbarCode = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Shell", "Panels", "Taskbar.xaml.cs"));
        int opening = windowManager.IndexOf("_openingTaskbars = pendingTaskbars;", StringComparison.Ordinal);
        if (!appBarWindow.Contains("protected internal virtual NativeMethods.Rect GetDesiredRect()") ||
            !taskbarCode.Contains("protected override NativeMethods.Rect GetDesiredRect() =>") || !taskbarCode.Contains("windowManager?.PlannedStandaloneRect(this)") ||
            opening < 0 || opening > windowManager.IndexOf("taskbar.Show();", StringComparison.Ordinal) || !windowManager.Contains("_openingTaskbars = null;"))
            throw new Exception("A panel of the manual layout must be shown at its planned rect, not across its whole side.");
        Console.WriteLine("PASS: panels of the manual layout are shown at their final rect.");
        // Our own ABM_SETSTATE goes through the forward queue: waiting on Explorer held the start for seconds.
        string trayService = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Support", "vendor", "ManagedShell", "src", "ManagedShell.WindowsTray", "TrayService.cs"));
        if (!trayService.Contains("amd.dwMessage == (int)ABMsg.ABM_SETSTATE && amd.dwSourceProcessId == Environment.ProcessId"))
            throw new Exception("The program's own ABM_SETSTATE must not wait for Explorer.");
        // Modern Glass panels are a little taller than the base 28 (they looked cut off).
        string modernGlass = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Assets", "Themes", "Other", "Modern Glass.xaml"));
        if (!modernGlass.Contains("<system:Double x:Key=\"TaskbarHeight\">33.34</system:Double>"))
            throw new Exception("Modern Glass sets its own panel height (40 px at scale 1.2).");
        Console.WriteLine("PASS: the own ABM_SETSTATE is queued; Modern Glass panels are 40 px high at scale 1.2.");
        // Explorer's imported tray icons must not overwrite an image the application already gave us: its HICON may be
        // destroyed and reused by then (black or dashed squares after a start).
        string notificationArea = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Support", "vendor", "ManagedShell", "src", "ManagedShell.WindowsTray", "NotificationArea.cs"));
        if (!notificationArea.Contains("_explorerTrayService.SetSystrayCallback(ImportFromExplorer);") ||
            !notificationArea.Contains("FindIcon(nicData)?.HasOwnImage == true") ||
            !notificationArea.Contains("trayIcon.HasOwnImage |= !_importingFromExplorer;"))
            throw new Exception("Explorer's tray import must skip icons that already have their application's image.");
        Console.WriteLine("PASS: Explorer's tray import keeps the image an application already gave.");
        // The windows' desktops are asked off the UI thread at start; a forgotten window drops its prefetched answer too.
        string desktops = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Core", "Desktop", "VirtualDesktopContext.cs"));
        int forget = desktops.IndexOf("internal void ForgetWindowDesktop(IntPtr hwnd)", StringComparison.Ordinal);
        if (!app.Contains("VirtualDesktopContext.Prefetch(") || app.IndexOf("VirtualDesktopContext.Prefetch(", StringComparison.Ordinal) > app.IndexOf("loadTheme();", StringComparison.Ordinal) ||
            !desktops.Contains("Prefetched.TryRemove(hwnd, out Guid id)") || forget < 0 ||
            desktops.IndexOf("Prefetched.TryRemove(hwnd, out _);", forget, StringComparison.Ordinal) < 0 ||
            desktops.Contains("desktopForWindowCache.Remove(hwnd);\n                    return true;"))
            throw new Exception("Window desktops are prefetched before the theme loads, and forgetting a window forgets its prefetched desktop.");
        Console.WriteLine("PASS: window desktops are prefetched off the UI thread at start.");
        // Across a restart on purpose, the strips of the last run are held from Main until the new panels published theirs.
        var strips = ReservationBridge.StripsFor(new ManagedShell.Interop.NativeMethods.Rect(148, 40, 1772, 1040), new ManagedShell.Interop.NativeMethods.Rect(0, 0, 1920, 1080));
        if (strips.Length != 4 ||
            !strips[0].Rect.Equals(new ManagedShell.Interop.NativeMethods.Rect(0, 0, 148, 1080)) ||
            !strips[1].Rect.Equals(new ManagedShell.Interop.NativeMethods.Rect(0, 0, 1920, 40)) ||
            !strips[2].Rect.Equals(new ManagedShell.Interop.NativeMethods.Rect(1772, 0, 1920, 1080)) ||
            !strips[3].Rect.Equals(new ManagedShell.Interop.NativeMethods.Rect(0, 1040, 1920, 1080)))
            throw new Exception("The held strips are the bands between the screen's edges and the work area.");
        if (ReservationBridge.StripsFor(new ManagedShell.Interop.NativeMethods.Rect(0, 0, 1920, 1040), new ManagedShell.Interop.NativeMethods.Rect(0, 0, 1920, 1080)).Length != 1)
            throw new Exception("An edge without a strip holds nothing.");
        int bridgeStart = program.IndexOf("StartReservationBridge();", StringComparison.Ordinal);
        if (bridgeStart < 0 || bridgeStart > program.IndexOf("new App()", StringComparison.Ordinal) ||
            !program.Contains("RestartMarker.IsFresh() || Settings.Instance.AutoHide") ||
            windowManager.IndexOf("ReservationBridge.Release();", StringComparison.Ordinal) < windowManager.IndexOf("ApplyManualLayout(pendingTaskbars, true);", StringComparison.Ordinal))
            throw new Exception("The bridge starts in Main on a restart on purpose and is released after the panels published their reservations.");
        // The deploy script holds the strips while the old instance dies; the new one tells it when it holds them itself.
        string holder = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Support", "BuildTools", "scripts", "Hold-WorkAreaStrips.ps1"));
        if (!holder.Contains(@"Local\UltraWinBarStripsHeld") || ReservationBridge.HeldEventName != @"Local\UltraWinBarStripsHeld" ||
            script.IndexOf("Hold-WorkAreaStrips.ps1", StringComparison.Ordinal) < 0 ||
            script.IndexOf("Hold-WorkAreaStrips.ps1", StringComparison.Ordinal) > script.IndexOf("Stop-Process", StringComparison.Ordinal) ||
            program.IndexOf("StartReservationBridge();", StringComparison.Ordinal) > program.IndexOf("SingleInstanceCheck()", StringComparison.Ordinal))
            throw new Exception("The strips are held before the old instance stops (deploy) or exits (Restart menu).");
        string taskbarAppBar = System.IO.File.ReadAllText(System.IO.Path.Combine(sources, "Shell", "Panels", "Taskbar.AppBar.cs"));
        if (!taskbarAppBar.Contains("if (_reserved && !RestartMarker.IsFresh())"))
            throw new Exception("Closing for a restart on purpose must not release the panel's strip.");
        Console.WriteLine("PASS: a restart on purpose holds the last run's strips until the new panels have theirs.");
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

    // Regression: dragging a panel edge stepped one row per queued mouse event and laid the panels out with the
    // previous size, so the panel trailed the pointer by seconds and settled at the wrong size.
    private static void RunResizeChecks(System.IO.DirectoryInfo repositoryRoot)
    {
        // Right panel at scale 1.2: base 116 + one 36 px column + 6 px unlocked margin = 158 px for two columns.
        (double distance, double dpi, int expected)[] cases =
        {
            (158, 1, 2), (175, 1, 2), (176, 1, 3), (194, 1, 3), (139, 1, 1), (141, 1, 2),
            (0, 1, 1), (-50, 1, 1), (5000, 1, 7), (237, 1.5, 2), (264, 1.5, 3),
        };
        foreach (var c in cases)
        {
            int actual = PanelLayout.SizeCountForDistance(c.distance, c.dpi, 116, 36, 6, 7);
            if (actual != c.expected)
                throw new Exception($"Resize count for {c.distance}px at {c.dpi}x: expected {c.expected}, got {actual}");
        }
        if (PanelLayout.SizeCountForDistance(500, 1, 116, 0, 6, 7) != 1 || PanelLayout.SizeCountForDistance(500, 0, 116, 36, 6, 7) != 1 ||
            PanelLayout.SizeCountForDistance(5000, 1, 116, 36, 6, 0) != 1)
            throw new Exception("Degenerate resize inputs must fall back to one row.");

        // Each ABM_SETPOS cost the shell 10-370 ms: publish only new rects, first registrations or shell requests.
        var rect = new ManagedShell.Interop.NativeMethods.Rect { Left = 1762, Top = 0, Right = 1920, Bottom = 1080 };
        var moved = new ManagedShell.Interop.NativeMethods.Rect { Left = 1726, Top = 0, Right = 1920, Bottom = 1080 };
        if (PanelReservation.ShouldPublish(true, rect, rect, false) || !PanelReservation.ShouldPublish(true, rect, moved, false) ||
            !PanelReservation.ShouldPublish(false, rect, rect, false) || !PanelReservation.ShouldPublish(true, null, rect, false) ||
            !PanelReservation.ShouldPublish(true, rect, rect, true))
            throw new Exception("Panel reservation must be republished only for a new rect, a first registration or a shell request.");

        string root = System.IO.Path.Combine(repositoryRoot.FullName, "UltraWinBar");
        string manager = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Core", "Desktop", "WindowManager.cs"));
        int branch = manager.IndexOf("e.PropertyName == nameof(Settings.EdgeSizes)))", StringComparison.Ordinal);
        int refresh = manager.IndexOf("RefreshDesiredSize()", branch, StringComparison.Ordinal);
        int apply = manager.IndexOf("ApplyManualLayout(_taskbars, false)", branch, StringComparison.Ordinal);
        if (branch < 0 || refresh < 0 || apply < 0 || refresh > apply)
            throw new Exception("Manual layout must refresh every panel's size before laying the panels out.");
        string drag = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Shell", "Panels", "Taskbar.PanelDrag.cs"));
        if (!drag.Contains("if (!_resizePending)") || !drag.Contains("PanelLayout.SizeCountForDistance(") || drag.Contains("TaskbarWidthCount += 1"))
            throw new Exception("Panel resize must coalesce pointer moves and jump to the size under the pointer.");
        string panel = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Shell", "Panels", "Taskbar.xaml.cs"));
        int layout = panel.IndexOf("internal void SetStandaloneLayout", StringComparison.Ordinal);
        int layoutEnd = panel.IndexOf('}', layout);
        if (layout < 0 || !panel.Substring(layout, layoutEnd - layout).Contains("ScheduleReservation()") ||
            panel.Substring(layout, layoutEnd - layout).Contains("PanelReservation.Reserve("))
            throw new Exception("A resize step must defer the shell reservation until the layout settles.");
        Console.WriteLine("PASS: panel resize jumps to the size nearest the pointer (any DPI, clamped), coalesces moves, and lays out with the fresh size.");
    }
}
