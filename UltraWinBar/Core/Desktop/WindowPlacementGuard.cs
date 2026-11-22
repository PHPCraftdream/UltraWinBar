using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using ManagedShell.Common.Native;
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
    // Keeps the manual work area and the maximized windows fitted to the panels. A restored window is never moved or
    // resized while its title bar can be grabbed: it may be dragged past the panels and off the work area. A window
    // whose title bar cannot be reached (under a panel, off every monitor) cannot be dragged back, so it is moved
    // into the work area, size kept, when it is activated (or shown partly on a monitor). Full-screen windows stay.
    internal sealed class WindowPlacementGuard : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out NativeMethods.Rect value, int size);

        // Part of the title bar that must be within the mouse's reach for the window to be draggable.
        private const int GrabWidth = 48, GrabHeight = 12;
        private const int WsCaption = 0x00C00000, WsSysMenu = 0x00080000, WsMinMaxBox = 0x00030000;
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly Action requestWorkAreaRecovery;
        // R9-H: single-event, UI-thread registrations shared via the hub with the other subscribers of the same event+flags.
        private readonly IDisposable foregroundSubscription, showSubscription;
        private readonly DispatcherTimer refitTimer, reachTimer;
        // Windows to look at, and whether they were activated (not only shown) meanwhile.
        private readonly Dictionary<IntPtr, bool> pendingReach = new Dictionary<IntPtr, bool>();
        private readonly Dictionary<IntPtr, long> lastReach = new Dictionary<IntPtr, long>();
        // Work area the maximized windows were last fitted to (panel resize), besides the full monitor.
        private NativeMethods.Rect? refitFrom;
        private bool disposed;

        public WindowPlacementGuard(Action requestWorkAreaRecovery)
        {
            this.requestWorkAreaRecovery = requestWorkAreaRecovery;
            foregroundSubscription = WinEventHub.Subscribe("Window placement foreground hook", 3, OnForeground);
            showSubscription = WinEventHub.Subscribe("Window placement show hook", 0x8002, OnShown, WinEventHook.SkipOwnProcess);
            refitTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(1500) };
            refitTimer.Tick += (_, _) => { refitTimer.Stop(); RefitMaximizedWindows(); };
            // A window places itself right after it is shown: look at it once it has settled.
            reachTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
            reachTimer.Tick += (_, _) => { reachTimer.Stop(); BringPendingIntoReach(); };
        }

        internal static Rect AvailableArea(System.Drawing.Rectangle bounds)
        {
            var result = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (Settings.Instance.AutoHide) return result;
            foreach (var bar in Application.Current.Windows.OfType<Taskbar>())
            {
                if (bar.IsClosing || bar.Screen.Bounds != bounds) continue;
                var r = bar.WindowRect;
                switch (bar.AppBarEdge)
                {
                    case AppBarEdge.Left: result.X = Math.Max(result.Left, r.Right); result.Width = Math.Max(1, bounds.Right - result.X); break;
                    case AppBarEdge.Top: result.Y = Math.Max(result.Top, r.Bottom); result.Height = Math.Max(1, bounds.Bottom - result.Y); break;
                }
            }
            foreach (var bar in Application.Current.Windows.OfType<Taskbar>())
            {
                if (bar.IsClosing || bar.Screen.Bounds != bounds) continue;
                if (bar.AppBarEdge == AppBarEdge.Right) result.Width = Math.Max(1, Math.Min(result.Right, bar.WindowRect.Left) - result.Left);
                if (bar.AppBarEdge == AppBarEdge.Bottom) result.Height = Math.Max(1, Math.Min(result.Bottom, bar.WindowRect.Top) - result.Top);
            }
            return result;
        }

        // A window maximized into "reference" (the monitor after Windows cleared the work area, or our previous work
        // area after a panel resize) is not re-maximized by Windows: move it into "area", keeping its frame overhang.
        internal static NativeMethods.Rect? PlanMaximizedRefit(NativeMethods.Rect outer, NativeMethods.Rect reference, Rect area)
        {
            int left = reference.Left - outer.Left, top = reference.Top - outer.Top;
            int right = outer.Right - reference.Right, bottom = outer.Bottom - reference.Bottom;
            if (left < 0 || top < 0 || right < 0 || bottom < 0 || Math.Max(Math.Max(left, top), Math.Max(right, bottom)) > 64) return null;
            if (area.Left == reference.Left && area.Top == reference.Top && area.Right == reference.Right && area.Bottom == reference.Bottom) return null;
            return new NativeMethods.Rect
            {
                Left = (int)area.Left - left, Top = (int)area.Top - top,
                Right = (int)area.Right + right, Bottom = (int)area.Bottom + bottom
            };
        }

        // The title bar is the window's only handle, but not all of it: the icon and the caption buttons (the first
        // "leftInset" and the last "rightInset" pixels) cannot drag. When no draggable part of at least
        // GrabWidth x GrabHeight lies in an area the mouse reaches (monitors minus panels), the window cannot be moved by
        // the user: return its rect moved, size kept, into "work" (whole when it fits, else by its top left corner; a
        // window off every area goes to the middle). Null when the title bar can be grabbed. "screen" is the monitor
        // nearest to the window: one covering it is full-screen, and one wholly off it is moved only once activated
        // (a window merely shown there may be parked off-screen on purpose).
        internal static NativeMethods.Rect? PlanReach(NativeMethods.Rect window, int captionHeight, int leftInset, int rightInset,
            IReadOnlyList<Rect> reachable, Rect work, Rect screen, bool activated)
        {
            if (window.Width <= 0 || window.Height <= 0 || work.Width <= 0 || work.Height <= 0) return null;
            if (window.Left <= screen.Left && window.Top <= screen.Top && window.Right >= screen.Right && window.Bottom >= screen.Bottom) return null;
            if (!activated && !(window.Left < screen.Right && window.Right > screen.Left && window.Top < screen.Bottom && window.Bottom > screen.Top)) return null;
            double stripBottom = window.Top + Math.Min(captionHeight, window.Height);
            double grabLeft = window.Left + leftInset, grabRight = window.Right - rightInset;
            if (grabRight - grabLeft < 1) { grabLeft = window.Left; grabRight = window.Right; }
            double needWidth = Math.Min(GrabWidth, grabRight - grabLeft), needHeight = Math.Min(GrabHeight, stripBottom - window.Top);
            foreach (var area in reachable)
                if (Math.Min(grabRight, area.Right) - Math.Max(grabLeft, area.Left) >= needWidth &&
                    Math.Min(stripBottom, area.Bottom) - Math.Max(window.Top, area.Top) >= needHeight)
                    return null;
            bool overlapsWork = window.Left < work.Right && window.Right > work.Left && window.Top < work.Bottom && window.Bottom > work.Top;
            double x, y;
            if (overlapsWork)
            {
                x = window.Width <= work.Width ? Math.Max(work.Left, Math.Min(window.Left, work.Right - window.Width)) : work.Left;
                y = window.Height <= work.Height ? Math.Max(work.Top, Math.Min(window.Top, work.Bottom - window.Height)) : work.Top;
            }
            else
            {
                x = work.Left + Math.Max(0, (work.Width - window.Width) / 2);
                y = work.Top + Math.Max(0, (work.Height - window.Height) / 2);
            }
            int left = (int)Math.Round(x), top = (int)Math.Round(y);
            if (left == window.Left && top == window.Top) return null;
            return new NativeMethods.Rect { Left = left, Top = top, Right = left + window.Width, Bottom = top + window.Height };
        }

        // Moves a window the user activates (its taskbar button, the hotkey) into the work area when the mouse cannot
        // reach its title bar. False when it was left alone.
        internal static bool BringIntoReach(IntPtr hwnd) => BringIntoReach(hwnd, true, null);

        private static List<Rect> ReachableAreas() =>
            System.Windows.Forms.Screen.AllScreens.Select(screen => AvailableArea(screen.Bounds)).ToList();

        private static bool BringIntoReach(IntPtr hwnd, bool activated, List<Rect> reachable)
        {
            if (!IsMovable(hwnd, out NativeMethods.Rect outer, out int style, out int exStyle)) return false;
            var (height, leftInset, rightInset) = CaptionOf(hwnd, outer, style, exStyle);
            reachable ??= ReachableAreas();
            var home = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(outer.Left, outer.Top, outer.Width, outer.Height)).Bounds;
            var target = PlanReach(outer, height, leftInset, rightInset, reachable, AvailableArea(home),
                new Rect(home.Left, home.Top, home.Width, home.Height), activated);
            if (!target.HasValue) return false;
            // Async: a hung application must not block the UI thread. Elevated windows refuse (UIPI).
            bool queued = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, target.Value.Left, target.Value.Top, 0, 0,
                (int)(NativeMethods.SetWindowPosFlags.SWP_NOSIZE | NativeMethods.SetWindowPosFlags.SWP_NOZORDER |
                      NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE | NativeMethods.SetWindowPosFlags.SWP_ASYNCWINDOWPOS));
            var className = new StringBuilder(64);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            ShellLogger.Info($"WindowPlacementGuard: title bar of window 0x{(long)hwnd:X} (class '{className}', process {processId}) was out of reach at " +
                $"({outer.Left},{outer.Top})-({outer.Right},{outer.Bottom}); moved to ({target.Value.Left},{target.Value.Top}){(queued ? "" : ", refused")}");
            return queued;
        }

        private static bool IsMovable(IntPtr hwnd, out NativeMethods.Rect outer, out int style, out int exStyle)
        {
            outer = default;
            style = exStyle = 0;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || GetAncestor(hwnd, 2) != hwnd) return false;
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) || IsZoomed(hwnd)) return false;
            style = NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_STYLE);
            exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_EXSTYLE);
            if ((style & WsCaption) != WsCaption || (exStyle & (int)NativeMethods.ExtendedWindowStyles.WS_EX_TRANSPARENT) != 0) return false;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == Environment.ProcessId) return false;
            // Cloaked also means on another virtual desktop; no cross-process IVirtualDesktopManager call here.
            NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, out uint cloaked, sizeof(uint));
            return cloaked == 0 && NativeMethods.GetWindowRect(hwnd, out outer);
        }

        // Height of the title bar and the widths of what cannot drag on its left (icon) and right (caption buttons).
        private static (int Height, int Left, int Right) CaptionOf(IntPtr hwnd, NativeMethods.Rect outer, int style, int exStyle)
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;
            bool tool = (exStyle & (int)NativeMethods.ExtendedWindowStyles.WS_EX_TOOLWINDOW) != 0, menu = (style & WsSysMenu) != 0;
            // SM_CYCAPTION or SM_CYSMCAPTION, SM_CYSIZEFRAME, SM_CXPADDEDBORDER.
            int height = GetSystemMetricsForDpi(tool ? 51 : 4, dpi) + GetSystemMetricsForDpi(33, dpi) + GetSystemMetricsForDpi(92, dpi);
            int left = menu && !tool ? height : 0;
            // DWMWA_CAPTION_BUTTON_BOUNDS, relative to the window; else the usual 46 px per button.
            int right = DwmGetWindowAttribute(hwnd, 5, out var buttons, Marshal.SizeOf<NativeMethods.Rect>()) == 0 && buttons.Right > buttons.Left
                ? Math.Max(0, outer.Width - buttons.Left)
                : !menu ? 0 : tool ? height : (int)(46L * dpi / 96) * ((style & WsMinMaxBox) != 0 ? 3 : 1);
            return (height, left, right);
        }

        // Now and once more after Windows' own late re-layout.
        internal void ScheduleMaximizedRefit(NativeMethods.Rect? previousArea = null)
        {
            if (disposed) return;
            refitFrom = previousArea;
            dispatcher.BeginInvoke(new Action(RefitMaximizedWindows), DispatcherPriority.Background);
            refitTimer.Stop();
            refitTimer.Start();
        }

        private void RefitMaximizedWindows()
        {
            if (disposed) return;
            var bars = new HashSet<IntPtr>(Application.Current.Windows.OfType<Taskbar>().Select(bar => bar.Handle));
            var maximized = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                if (IsZoomed(hwnd) && NativeMethods.IsWindowVisible(hwnd) && !bars.Contains(hwnd) &&
                    (NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_STYLE) & WsCaption) == WsCaption)
                    maximized.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            int refitted = 0, denied = 0;
            foreach (var hwnd in maximized)
            {
                if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect outer)) continue;
                var bounds = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
                var monitor = new NativeMethods.Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
                var area = AvailableArea(bounds);
                var target = PlanMaximizedRefit(outer, monitor, area) ?? (refitFrom.HasValue ? PlanMaximizedRefit(outer, refitFrom.Value, area) : null);
                if (!target.HasValue) continue;
                // Async: a hung application must not block the UI thread.
                // Elevated windows refuse (UIPI) and stay stretched.
                if (NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, target.Value.Left, target.Value.Top, target.Value.Width, target.Value.Height,
                    (int)(NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE | NativeMethods.SetWindowPosFlags.SWP_NOZORDER |
                          NativeMethods.SetWindowPosFlags.SWP_ASYNCWINDOWPOS))) refitted++;
                else denied++;
            }
            if (refitted > 0 || denied > 0)
                ShellLogger.Info($"WindowPlacementGuard: Refitted {refitted} maximized window(s) to the panel work area; {denied} refused");
        }

        private void OnForeground(uint type, IntPtr hwnd, int obj, int child)
        {
            if (disposed || hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            requestWorkAreaRecovery?.Invoke();
            WatchReach(hwnd, true);
        }

        private void OnShown(uint type, IntPtr hwnd, int obj, int child)
        {
            if (disposed || hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            WatchReach(hwnd, false);
        }

        private void WatchReach(IntPtr hwnd, bool activated)
        {
            if (GetAncestor(hwnd, 2) != hwnd || (NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_STYLE) & WsCaption) != WsCaption) return;
            bool added = !pendingReach.TryGetValue(hwnd, out bool wasActivated);
            pendingReach[hwnd] = activated || wasActivated;
            // Every newly seen window gets the full settling time.
            if (added) { reachTimer.Stop(); reachTimer.Start(); }
        }

        private void BringPendingIntoReach()
        {
            if (disposed) return;
            var windows = pendingReach.ToArray();
            pendingReach.Clear();
            long now = Environment.TickCount64;
            List<Rect> reachable = null;
            foreach (var (hwnd, activated) in windows)
            {
                // A window that moves itself back is left alone for a while instead of being fought over.
                if (lastReach.TryGetValue(hwnd, out long at) && now - at < 2000) continue;
                if (!BringIntoReach(hwnd, activated, reachable ??= ReachableAreas())) continue;
                if (lastReach.Count > 64) lastReach.Clear();
                lastReach[hwnd] = now;
            }
        }

        public void Dispose()
        {
            disposed = true;
            refitTimer.Stop();
            reachTimer.Stop();
            foregroundSubscription.Dispose();
            showSubscription.Dispose();
        }
    }
}
