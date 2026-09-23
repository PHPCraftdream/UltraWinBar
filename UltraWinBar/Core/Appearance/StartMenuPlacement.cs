using ManagedShell.AppBar;
using ManagedShell.Interop;
using System;
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
    }
}
