using ManagedShell.AppBar;
using ManagedShell.Interop;
using System;
using System.Collections.Generic;

namespace UltraWinBar.Utilities
{
    public static class PanelLayout
    {
        public static (List<(AppBarEdge Edge, NativeMethods.Rect Bounds)> Panels, NativeMethods.Rect WorkArea)
            Calculate(NativeMethods.Rect screen, IEnumerable<AppBarEdge> order, Func<AppBarEdge, int> thicknessForEdge)
        {
            var panels = new List<(AppBarEdge, NativeMethods.Rect)>();
            var available = screen;

            foreach (AppBarEdge edge in order)
            {
                var bounds = available;
                switch (edge)
                {
                    case AppBarEdge.Left:
                        bounds.Right = bounds.Left + Math.Clamp(thicknessForEdge(edge), 0, available.Width);
                        available.Left = bounds.Right;
                        break;
                    case AppBarEdge.Right:
                        bounds.Left = bounds.Right - Math.Clamp(thicknessForEdge(edge), 0, available.Width);
                        available.Right = bounds.Left;
                        break;
                    case AppBarEdge.Top:
                        bounds.Bottom = bounds.Top + Math.Clamp(thicknessForEdge(edge), 0, available.Height);
                        available.Top = bounds.Bottom;
                        break;
                    case AppBarEdge.Bottom:
                        bounds.Top = bounds.Bottom - Math.Clamp(thicknessForEdge(edge), 0, available.Height);
                        available.Bottom = bounds.Top;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(order), edge, "Unsupported panel edge");
                }

                panels.Add((edge, bounds));
            }

            return (panels, available);
        }
    }
}
