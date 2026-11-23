using System;
using System.Windows;
using System.Windows.Controls;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The days of a month grid: seven equal columns and weeks of one height, that of the fullest day. Only when the month
    /// does not fit (the window would be higher than the screen) are the weeks with less in them made lower, down to what
    /// they need; if that is not enough, all of them in proportion, never under the lowest. Space left over is shared out.
    /// </summary>
    public sealed class WeekRowsPanel : Panel
    {
        private double[] rows = Array.Empty<double>();

        protected override Size MeasureOverride(Size available)
        {
            double column = double.IsInfinity(available.Width) ? 150 : available.Width / 7;
            rows = new double[(InternalChildren.Count + 6) / 7];
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                UIElement child = InternalChildren[i];
                child.Measure(new Size(column, double.PositiveInfinity));
                rows[i / 7] = Math.Max(rows[i / 7], child.DesiredSize.Height);
            }
            double uniform = 0;
            foreach (double row in rows) uniform = Math.Max(uniform, row);
            double total = uniform * rows.Length;
            return new Size(double.IsInfinity(available.Width) ? column * 7 : available.Width,
                double.IsInfinity(available.Height) ? total : Math.Min(total, available.Height));
        }

        protected override Size ArrangeOverride(Size final)
        {
            double[] heights = Fit(rows, final.Height);
            double column = final.Width / 7, top = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                if (i > 0 && i % 7 == 0) top += heights[i / 7 - 1];
                InternalChildren[i].Arrange(new Rect(column * (i % 7), top, column, heights[i / 7]));
            }
            return final;
        }

        /// <summary>
        /// The heights of the weeks in the height there is: all as high as the fullest; extra shared out evenly; a shortfall
        /// taken first from the weeks with less in them (down to what they need), then from all in proportion.
        /// </summary>
        internal static double[] Fit(double[] wanted, double height)
        {
            int count = wanted.Length;
            if (count == 0) return Array.Empty<double>();
            double uniform = 0, lowest = double.MaxValue;
            foreach (double row in wanted) { uniform = Math.Max(uniform, row); lowest = Math.Min(lowest, row); }
            var result = new double[count];
            for (int i = 0; i < count; i++) result[i] = uniform;
            if (double.IsNaN(height) || double.IsInfinity(height)) return result;

            double total = uniform * count;
            if (total <= height)
            {
                for (int i = 0; i < count; i++) result[i] += (height - total) / count;
                return result;
            }
            double excess = total - height, slack = 0;
            for (int i = 0; i < count; i++) slack += uniform - wanted[i];
            if (slack > 0)
            {
                double take = Math.Min(excess, slack);
                for (int i = 0; i < count; i++) result[i] -= (uniform - wanted[i]) * take / slack;
                excess -= take;
            }
            if (excess > 0)
            {
                double now = 0;
                foreach (double row in result) now += row;
                double scale = (now - excess) / now;
                for (int i = 0; i < count; i++) result[i] = Math.Max(Math.Min(lowest, result[i]), result[i] * scale);
            }
            return result;
        }
    }
}
