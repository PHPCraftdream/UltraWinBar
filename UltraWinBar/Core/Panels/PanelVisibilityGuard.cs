using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    // After a logon (the shell starts together with Explorer) two of the four panels were once not shown at all while the
    // others were; a desktop switch brought them back and the log recorded nothing. Right after start-up this checks that
    // every panel can be seen where it is, writes down what is wrong with one that cannot, puts it right and, when that
    // does not help, opens the panels again.
    internal sealed class PanelVisibilityGuard : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        internal struct PanelState
        {
            public bool Visible;
            public uint Cloaked;
            public double Opacity;
            // False while the panel has conceded to a full-screen app: it is then meant to sit at the bottom.
            public bool ExpectOnTop;
            public bool Topmost;
            public bool Reachable;
        }

        // Checks after start-up, each delay counted from the previous check; the logon race is over within seconds.
        internal static readonly TimeSpan[] Schedule =
        {
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(24), TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(84),
        };

        private const int MaxReopens = 1;
        private readonly Func<IEnumerable<Taskbar>> panels;
        private readonly Action reopenPanels;
        private readonly DispatcherTimer timer;
        private int step;
        private int failedChecks;
        private int reopens;
        private bool disposed;

        internal PanelVisibilityGuard(Func<IEnumerable<Taskbar>> panels, Action reopenPanels)
        {
            this.panels = panels;
            this.reopenPanels = reopenPanels;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Schedule[0] };
            timer.Tick += (_, _) => Tick();
            Restart();
        }

        // New panels (first start, or opened again): the checks begin anew, the reopen budget does not.
        internal void Restart()
        {
            if (disposed) return;
            step = 0;
            failedChecks = 0;
            timer.Stop();
            timer.Interval = Schedule[0];
            timer.Start();
        }

        internal static string Problems(PanelState state)
        {
            var found = new List<string>();
            if (!state.Visible) found.Add("not visible");
            if (state.Cloaked != 0) found.Add($"cloaked ({state.Cloaked})");
            if (state.Opacity < 1) found.Add($"opacity {state.Opacity:0.##}");
            if (state.ExpectOnTop && !state.Topmost) found.Add("not topmost");
            if (state.ExpectOnTop && state.Visible && state.Cloaked == 0 && !state.Reachable) found.Add("covered");
            return string.Join(", ", found);
        }

        // What to do about the n-th check in a row that found a problem: put the panel right first, then open the
        // panels again (a few times at most), then only report.
        internal static string ActionFor(int failedChecksInARow, int reopensDone) =>
            failedChecksInARow <= 1 ? "reassert" : failedChecksInARow == 2 && reopensDone < MaxReopens ? "reopen" : "report";

        private void Tick()
        {
            timer.Stop();
            if (disposed) return;
            try { Check(); }
            catch (Exception error) { ShellLogger.Warning($"PanelVisibilityGuard: check failed: {error.Message}"); }
            if (disposed || ++step >= Schedule.Length) return;
            timer.Interval = Schedule[step];
            timer.Start();
        }

        private void Check()
        {
            if (Settings.Instance.AutoHide) return;
            var checkedPanels = panels().Where(panel => !panel.IsClosing && panel.Handle != IntPtr.Zero).ToList();
            var broken = new List<Taskbar>();
            foreach (Taskbar panel in checkedPanels)
            {
                string problems = Problems(Probe(panel, out string covering));
                if (problems.Length == 0) continue;
                broken.Add(panel);
                ShellLogger.Warning($"PanelVisibilityGuard: {panel.AppBarEdge} panel is {problems}{covering}");
            }

            if (broken.Count == 0)
            {
                if (failedChecks > 0) ShellLogger.Info("PanelVisibilityGuard: every panel can be seen again.");
                failedChecks = 0;
                return;
            }

            failedChecks++;
            string action = ActionFor(failedChecks, reopens);
            if (action == "reassert")
            {
                foreach (Taskbar panel in broken) Reassert(panel);
                ShellLogger.Warning($"PanelVisibilityGuard: {broken.Count} panel(s) put right.");
            }
            else if (action == "reopen")
            {
                reopens++;
                ShellLogger.Warning("PanelVisibilityGuard: still not right; opening the panels again.");
                reopenPanels();
            }
        }

        private static PanelState Probe(Taskbar panel, out string covering)
        {
            covering = "";
            IntPtr handle = panel.Handle;
            NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, out uint cloaked, sizeof(uint));
            int style = NativeMethods.GetWindowLong(handle, NativeMethods.WindowLongFlags.GWL_EXSTYLE);
            var state = new PanelState
            {
                Visible = NativeMethods.IsWindowVisible(handle),
                Cloaked = cloaked,
                Opacity = panel.Opacity,
                ExpectOnTop = panel.Topmost,
                Topmost = (style & (int)NativeMethods.ExtendedWindowStyles.WS_EX_TOPMOST) != 0,
                Reachable = true,
            };
            if (state.ExpectOnTop && state.Visible && state.Cloaked == 0 && NativeMethods.GetWindowRect(handle, out NativeMethods.Rect rect))
            {
                state.Reachable = IsReachable(handle, rect, out IntPtr cover);
                if (!state.Reachable) covering = " by " + Describe(cover);
            }
            return state;
        }

        // The panel is seen when it is the window under at least one of five points of it; a transparent patch may let
        // a point through, a covered or hidden panel lets all of them through.
        private static bool IsReachable(IntPtr panel, NativeMethods.Rect rect, out IntPtr firstCover)
        {
            firstCover = IntPtr.Zero;
            int centerX = (rect.Left + rect.Right) / 2, centerY = (rect.Top + rect.Bottom) / 2;
            int quarterX = rect.Width / 4, quarterY = rect.Height / 4;
            foreach (var (x, y) in new[] { (centerX, centerY), (centerX - quarterX, centerY), (centerX + quarterX, centerY),
                (centerX, centerY - quarterY), (centerX, centerY + quarterY) })
            {
                IntPtr hit = WindowFromPoint(new NativePoint { X = x, Y = y });
                IntPtr root = hit == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hit, 2);
                if (root == panel) return true;
                if (firstCover == IntPtr.Zero) firstCover = root;
            }
            return false;
        }

        private static string Describe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "nothing";
            var className = new StringBuilder(64);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            return $"0x{(long)hwnd:X} class '{className}' process {processId}";
        }

        private static void Reassert(Taskbar panel)
        {
            panel.Opacity = 1;
            if (panel.Visibility != Visibility.Visible) panel.Visibility = Visibility.Visible;
            if (!NativeMethods.IsWindowVisible(panel.Handle))
                NativeMethods.ShowWindow(panel.Handle, NativeMethods.WindowShowStyle.ShowNoActivate);
            if (panel.Topmost)
                NativeMethods.SetWindowPos(panel.Handle, (IntPtr)NativeMethods.WindowZOrder.HWND_TOPMOST, 0, 0, 0, 0,
                    (int)(NativeMethods.SetWindowPosFlags.SWP_NOMOVE | NativeMethods.SetWindowPosFlags.SWP_NOSIZE |
                          NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE));
        }

        public void Dispose()
        {
            disposed = true;
            timer.Stop();
        }
    }
}
