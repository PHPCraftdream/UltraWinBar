using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
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
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out NativeMethods.Rect value, int size);
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly HashSet<IntPtr> pending = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, (NativeMethods.Rect Outer, Rect Area, long At)> lastAttempt = new Dictionary<IntPtr, (NativeMethods.Rect, Rect, long)>();
        private readonly Action requestWorkAreaRecovery;
        private readonly WinEventHook showHook, foregroundHook, moveHook;
        private IntPtr movingWindow;
        private bool disposed;

        public WindowPlacementGuard(Action requestWorkAreaRecovery)
        {
            this.requestWorkAreaRecovery = requestWorkAreaRecovery;
            showHook = new WinEventHook("Window placement show hook", 0x8002, 0x8002, HandleWindowEvent);
            foregroundHook = new WinEventHook("Window placement foreground hook", 3, 3, HandleWindowEvent);
            moveHook = new WinEventHook("Window placement move hook", 0x000A, 0x000B, HandleWindowEvent);
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
            showHook.Dispose();
            foregroundHook.Dispose();
            moveHook.Dispose();
        }
    }
}
