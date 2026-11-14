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
    // Keeps the manual work area and the maximized windows fitted to the panels. Restored windows are
    // never moved or resized: they may be dragged past the panels and off the work area.
    internal sealed class WindowPlacementGuard : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly Action requestWorkAreaRecovery;
        // R9-H: a single-event, UI-thread registration shared via the hub with the other subscribers of the same event+flags.
        private readonly IDisposable foregroundSubscription;
        private readonly DispatcherTimer refitTimer;
        // Work area the maximized windows were last fitted to (panel resize), besides the full monitor.
        private NativeMethods.Rect? refitFrom;
        private bool disposed;

        public WindowPlacementGuard(Action requestWorkAreaRecovery)
        {
            this.requestWorkAreaRecovery = requestWorkAreaRecovery;
            foregroundSubscription = WinEventHub.Subscribe("Window placement foreground hook", 3, OnForeground);
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
        }

        public void Dispose()
        {
            disposed = true;
            refitTimer.Stop();
            foregroundSubscription.Dispose();
        }
    }
}
