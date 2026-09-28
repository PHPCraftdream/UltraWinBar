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
