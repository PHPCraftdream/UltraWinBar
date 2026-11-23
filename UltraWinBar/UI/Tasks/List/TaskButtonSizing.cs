using System;

namespace UltraWinBar.Controls
{
    // Button width, extra pixels and scrolling for a horizontal panel's task buttons.
    internal readonly record struct TaskButtonSizing(double ButtonWidth, int ExtraWidthCount, int ButtonsPerRow, bool Scrollable)
    {
        // The height of a row of buttons on a horizontal panel: the list's height shared by its rows, in whole pixels;
        // NaN (the content's height) while the list is not laid out or has no rows.
        internal static double RowHeight(double listHeight, int rows) => listHeight > 0 && rows > 0 ? Math.Floor(listHeight / rows) : double.NaN;

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
