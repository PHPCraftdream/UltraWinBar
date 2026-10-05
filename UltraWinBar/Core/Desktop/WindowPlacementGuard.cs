using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using ManagedShell.Common.Native;
using ManagedShell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    internal sealed class WindowPlacementGuard : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out NativeMethods.Rect value, int size);
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly HashSet<IntPtr> pending = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, (NativeMethods.Rect Outer, Rect Area, long At)> lastAttempt = new Dictionary<IntPtr, (NativeMethods.Rect, Rect, long)>();
        private readonly Action requestWorkAreaRecovery;
        // R9-H: show/foreground are single-event, UI-thread registrations shared via the hub with
        // the other subscribers of the same event+flags (review section 5); move is a range hook
        // (MOVESIZESTART..MOVESIZEEND) and stays a direct WinEventHook.
        private readonly IDisposable showSubscription, foregroundSubscription;
        private readonly WinEventHook moveHook;
        private readonly DispatcherTimer refitTimer;
        private IntPtr movingWindow;
        private bool disposed;

        public WindowPlacementGuard(Action requestWorkAreaRecovery)
        {
            this.requestWorkAreaRecovery = requestWorkAreaRecovery;
            showSubscription = WinEventHub.Subscribe("Window placement show hook", 0x8002, HandleWindowEvent);
            foregroundSubscription = WinEventHub.Subscribe("Window placement foreground hook", 3, HandleWindowEvent);
            moveHook = new WinEventHook("Window placement move hook", 0x000A, 0x000B, HandleWindowEvent);
            refitTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(1500) };
            refitTimer.Tick += (_, _) => { refitTimer.Stop(); RefitMaximizedWindows(); };
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

        internal static Rect? PlanPlacement(NativeMethods.Rect visible, Rect area, bool maximized)
        {
            if (maximized) return null;
            double width = Math.Min(visible.Width, area.Width), height = Math.Min(visible.Height, area.Height);
            double x = Math.Max(area.Left, Math.Min(visible.Left, area.Right - width));
            double y = Math.Max(area.Top, Math.Min(visible.Top, area.Bottom - height));
            if (visible.Left == x && visible.Top == y && visible.Width == width && visible.Height == height)
                return null;
            return new Rect(x, y, width, height);
        }

        // Windows re-maximizes into a cleared (full-monitor) work area and never shrinks back once ours is restored.
        internal static NativeMethods.Rect? PlanMaximizedRefit(NativeMethods.Rect outer, NativeMethods.Rect monitor, Rect area)
        {
            int left = monitor.Left - outer.Left, top = monitor.Top - outer.Top;
            int right = outer.Right - monitor.Right, bottom = outer.Bottom - monitor.Bottom;
            if (left < 0 || top < 0 || right < 0 || bottom < 0 || Math.Max(Math.Max(left, top), Math.Max(right, bottom)) > 64) return null;
            if (area.Left == monitor.Left && area.Top == monitor.Top && area.Right == monitor.Right && area.Bottom == monitor.Bottom) return null;
            return new NativeMethods.Rect
            {
                Left = (int)area.Left - left, Top = (int)area.Top - top,
                Right = (int)area.Right + right, Bottom = (int)area.Bottom + bottom
            };
        }

        // Now and once more after Windows' own late re-layout.
        internal void ScheduleMaximizedRefit()
        {
            if (disposed) return;
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
                    (NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_STYLE) & 0x00C00000) == 0x00C00000)
                    maximized.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            int refitted = 0, denied = 0;
            foreach (var hwnd in maximized)
            {
                if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect outer)) continue;
                var bounds = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
                var monitor = new NativeMethods.Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
                var target = PlanMaximizedRefit(outer, monitor, AvailableArea(bounds));
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

        private void HandleWindowEvent(uint type, IntPtr hwnd, int obj, int child)
        {
            if (disposed || hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            if (type == 3) requestWorkAreaRecovery?.Invoke();
            if (type == 0x000A) { movingWindow = hwnd; return; }
            if (type == 0x000B)
            {
                movingWindow = IntPtr.Zero;
                lastAttempt.Remove(hwnd);
            }
            if (movingWindow == hwnd) return;
            if (GetAncestor(hwnd, 2) != hwnd || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_STYLE);
            if ((style & 0x00C00000) != 0x00C00000) return;
            if (!pending.Add(hwnd)) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                pending.Remove(hwnd);
                if (!disposed) Constrain(hwnd);
            }), DispatcherPriority.Background);
        }

        private void Constrain(IntPtr hwnd)
        {
            if (VirtualDesktopContext.Instance?.IsOnCurrentDesktop(hwnd) == false) return;
            if (movingWindow == hwnd || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) ||
                GetAncestor(hwnd, 2) != hwnd) return;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_STYLE);
            if ((style & 0x00C00000) != 0x00C00000) return;
            if (Application.Current.Windows.OfType<Taskbar>().Any(bar => bar.Handle == hwnd)) return;
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect outer)) return;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var area = AvailableArea(screen.Bounds);
            long now = Environment.TickCount64;
            if (lastAttempt.TryGetValue(hwnd, out var previous) &&
                (now - previous.At < 1000 || previous.Outer.Equals(outer) && previous.Area.Equals(area))) return;
            var visible = outer;
            if (DwmGetWindowAttribute(hwnd, 9, out var frame, Marshal.SizeOf<NativeMethods.Rect>()) == 0 &&
                frame.Width > 0 && frame.Height > 0) visible = frame;
            var target = PlanPlacement(visible, area, IsZoomed(hwnd));
            if (!target.HasValue) return;
            if (lastAttempt.Count > 256) lastAttempt.Clear();
            lastAttempt[hwnd] = (outer, area, now);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                (int)target.Value.X - (visible.Left - outer.Left), (int)target.Value.Y - (visible.Top - outer.Top),
                (int)target.Value.Width + outer.Width - visible.Width, (int)target.Value.Height + outer.Height - visible.Height,
                (int)(NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE | NativeMethods.SetWindowPosFlags.SWP_NOZORDER));
        }

        public void Dispose()
        {
            disposed = true;
            refitTimer.Stop();
            showSubscription.Dispose();
            foregroundSubscription.Dispose();
            moveHook.Dispose();
        }
    }
}
