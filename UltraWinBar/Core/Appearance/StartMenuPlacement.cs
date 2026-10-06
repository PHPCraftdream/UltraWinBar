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
