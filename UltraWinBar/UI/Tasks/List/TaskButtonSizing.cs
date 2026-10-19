using System;

namespace UltraWinBar.Controls
{
    // Button width, extra pixels and scrolling for a horizontal panel's task buttons.
    internal readonly record struct TaskButtonSizing(double ButtonWidth, int ExtraWidthCount, int ButtonsPerRow, bool Scrollable)
    {
        // Null while the width is unknown (not laid out yet): sizing from 0 would shrink every button.
        internal static TaskButtonSizing? Compute(double availableWidth, int taskCount, int rows, double defaultWidth, double minWidth, double margin)
        {
            if (availableWidth <= 0)
                return null;

            int perRow = Math.Max(1, (int)Math.Ceiling((double)taskCount / Math.Max(1, rows)));
            double maxWidth = availableWidth / perRow;
            double fullWidth = defaultWidth + margin;
            double smallestWidth = minWidth + margin;

            // Room to spare: keep the default width and leave the trailing gap.
            if (maxWidth > fullWidth)
                return new TaskButtonSizing(fullWidth, 0, perRow, false);

            if (maxWidth < smallestWidth)
                return new TaskButtonSizing(Math.Ceiling(fullWidth / 2), 0, perRow, true);

            // Buttons are shrunk to fit a full row: spread the pixels lost to flooring
            // across the columns so the row fills the taskbar.
            double baseWidth = Math.Floor(maxWidth);
            return new TaskButtonSizing(baseWidth, (int)Math.Floor(availableWidth) - (int)baseWidth * perRow, perRow, false);
        }
    }
}
