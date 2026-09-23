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
        private delegate void WinEventProc(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out NativeMethods.Rect value, int size);
        private readonly WinEventProc callback;
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly HashSet<IntPtr> pending = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, NativeMethods.Rect> lastAttempt = new Dictionary<IntPtr, NativeMethods.Rect>();
        private readonly IntPtr showHook, foregroundHook, moveHook, locationHook;
        private IntPtr movingWindow;
        private bool disposed;

        public WindowPlacementGuard()
        {
            callback = OnWindowEvent;
            showHook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, callback, 0, 0, 0);
            foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
            moveHook = SetWinEventHook(0x000A, 0x000B, IntPtr.Zero, callback, 0, 0, 0);
            locationHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, 0, 0, 0);
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

        private void OnWindowEvent(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (disposed || hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            if (type == 0x000A) { movingWindow = hwnd; return; }
            if (type == 0x000B) movingWindow = IntPtr.Zero;
            if (movingWindow == hwnd) return;
            if (type != 0x800B) lastAttempt.Remove(hwnd);
            if (GetAncestor(hwnd, 2) != hwnd || !NativeMethods.IsWindowVisible(hwnd) || IsIconic(hwnd)) return;
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
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || IsIconic(hwnd) ||
                GetAncestor(hwnd, 2) != hwnd) return;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.WindowLongFlags.GWL_STYLE);
            if ((style & 0x00C00000) != 0x00C00000) return;
            if (Application.Current.Windows.OfType<Taskbar>().Any(bar => bar.Handle == hwnd)) return;
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect outer)) return;
            if (lastAttempt.TryGetValue(hwnd, out var previous) && previous.Equals(outer)) return;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var area = AvailableArea(screen.Bounds);
            var visible = outer;
            DwmGetWindowAttribute(hwnd, 9, out var frame, Marshal.SizeOf<NativeMethods.Rect>());
            if (frame.Width > 0 && frame.Height > 0) visible = frame;
            double width = Math.Min(visible.Width, area.Width), height = Math.Min(visible.Height, area.Height);
            double x = Math.Max(area.Left, Math.Min(visible.Left, area.Right - width));
            double y = Math.Max(area.Top, Math.Min(visible.Top, area.Bottom - height));
            if (visible.Left == x && visible.Top == y && visible.Width == width && visible.Height == height) return;
            // Borderless full-screen windows never enter this path.
            if (IsZoomed(hwnd))
            {
                x = area.Left; y = area.Top; width = area.Width; height = area.Height;
            }
            if (lastAttempt.Count > 256) lastAttempt.Clear();
            lastAttempt[hwnd] = outer;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                (int)x - (visible.Left - outer.Left), (int)y - (visible.Top - outer.Top),
                (int)width + outer.Width - visible.Width, (int)height + outer.Height - visible.Height,
                (int)(NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE | NativeMethods.SetWindowPosFlags.SWP_NOZORDER));
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var hook in new[] { showHook, foregroundHook, moveHook, locationHook })
                if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        }
    }
}
