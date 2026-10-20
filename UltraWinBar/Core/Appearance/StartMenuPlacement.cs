using ManagedShell.AppBar;
using ManagedShell.Interop;
using System;
using System.Collections.Generic;
using System.Windows;

namespace UltraWinBar.Utilities
{
    public static class StartMenuPlacement
    {
        public static Point GetTarget(NativeMethods.Rect menu, NativeMethods.Rect bar, Rect area, AppBarEdge edge, bool rtl)
        {
            double x = rtl ? bar.Right - menu.Width : bar.Left;
            double y = bar.Top;
            switch (edge)
            {
                case AppBarEdge.Left: x = bar.Right; break;
                case AppBarEdge.Right: x = bar.Left - menu.Width; break;
                case AppBarEdge.Top: y = bar.Bottom; break;
                case AppBarEdge.Bottom: y = bar.Top - menu.Height; break;
            }
            return new Point(Math.Max(area.Left, Math.Min(x, area.Right - menu.Width)),
                Math.Max(area.Top, Math.Min(y, area.Bottom - menu.Height)));
        }

        // Shell flyouts (Win+K, Win+P) dock to Explorer's hidden taskbar, not our panel: offset that
        // moves a flyout flush against Explorer's taskbar to the area edge instead. Null if not docked there.
        public static Vector? GetShellFlyoutShift(NativeMethods.Rect flyout, NativeMethods.Rect explorerTray, NativeMethods.Rect monitor, Rect area)
        {
            explorerTray = DockedPosition(explorerTray, monitor);
            bool spansHeight = explorerTray.Top <= monitor.Top && explorerTray.Bottom >= monitor.Bottom;
            bool spansWidth = explorerTray.Left <= monitor.Left && explorerTray.Right >= monitor.Right;
            double dx = 0, dy = 0;
            if (spansHeight && explorerTray.Right >= monitor.Right && explorerTray.Left > monitor.Left && flyout.Right == explorerTray.Left)
                dx = area.Right - flyout.Right;
            else if (spansHeight && explorerTray.Left <= monitor.Left && explorerTray.Right < monitor.Right && flyout.Left == explorerTray.Right)
                dx = area.Left - flyout.Left;
            else if (spansWidth && explorerTray.Bottom >= monitor.Bottom && explorerTray.Top > monitor.Top && flyout.Bottom == explorerTray.Top)
                dy = area.Bottom - flyout.Bottom;
            else if (spansWidth && explorerTray.Top <= monitor.Top && explorerTray.Bottom < monitor.Bottom && flyout.Top == explorerTray.Bottom)
                dy = area.Top - flyout.Top;
            return dx == 0 && dy == 0 ? null : new Vector(dx, dy);
        }

        // An auto-hidden Explorer taskbar is parked just past the screen edge, yet flyouts still dock at its full
        // thickness: where it sits when shown.
        private static NativeMethods.Rect DockedPosition(NativeMethods.Rect tray, NativeMethods.Rect monitor)
        {
            bool spansHeight = tray.Top <= monitor.Top && tray.Bottom >= monitor.Bottom;
            bool spansWidth = tray.Left <= monitor.Left && tray.Right >= monitor.Right;
            if (spansHeight == spansWidth) return tray;
            if (spansHeight)
                return tray.Left + tray.Right > monitor.Left + monitor.Right
                    ? new NativeMethods.Rect(monitor.Right - tray.Width, tray.Top, monitor.Right, tray.Bottom)
                    : new NativeMethods.Rect(monitor.Left, tray.Top, monitor.Left + tray.Width, tray.Bottom);
            return tray.Top + tray.Bottom > monitor.Top + monitor.Bottom
                ? new NativeMethods.Rect(tray.Left, monitor.Bottom - tray.Height, tray.Right, monitor.Bottom)
                : new NativeMethods.Rect(tray.Left, monitor.Top, tray.Right, monitor.Top + tray.Height);
        }

        // Panel to anchor a menu we did not open (Win key, other launchers): the Start-hosting panel on
        // the menu's monitor, else any panel there, else the primary Start-hosting panel. -1 if none.
        public static int ChooseAnchor(IReadOnlyList<(IntPtr Monitor, bool HostsStart, bool Primary)> bars, IntPtr menuMonitor)
        {
            int best = -1, bestRank = int.MaxValue;
            for (int i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                bool here = bar.Monitor == menuMonitor;
                int rank = here && bar.HostsStart ? 0 : here ? 1 : bar.Primary && bar.HostsStart ? 2 : bar.HostsStart ? 3 : 4;
                if (rank < bestRank) { best = i; bestRank = rank; }
            }
            return best;
        }
    }
}
