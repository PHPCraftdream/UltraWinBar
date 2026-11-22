using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace UltraWinBar.Controls
{
    // A rectangle of running lights around the element it overlays, shown while IsRunning. Cheap by construction:
    // while it is off nothing is drawn and no timer runs; while it is on, one timer redraws a few dozen dots
    // about ten times a second (no animation clock, nothing every frame); with animations switched off in Windows
    // or in a remote session the lights stand still.
    public sealed class MarqueeFrame : FrameworkElement
    {
        public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(MarqueeFrame),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((MarqueeFrame)d).Sync()));
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(MarqueeFrame),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

        // One lamp in GroupSize is lit and the one behind it glows, so the pattern closes on itself around the frame.
        internal const int GroupSize = 4;
        internal const double Spacing = 4.5, Inset = 1.2, LampRadius = 0.9;
        internal static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(100);
        private static readonly double[] LevelOpacity = { 0.25, 0.6, 1 };

        private DispatcherTimer timer;
        private Point[] lamps;
        private Size layoutSize;
        private int phase;

        public MarqueeFrame()
        {
            IsHitTestVisible = false;
            IsVisibleChanged += (_, _) => Sync();
            Loaded += (_, _) => Sync();
            Unloaded += (_, _) => { timer?.Stop(); phase = 0; };
        }

        internal static bool ShouldAnimate(bool isRunning, bool isShown, bool animationsAllowed) => isRunning && isShown && animationsAllowed;

        // Lamps at equal distances along the rectangle (clockwise from its top left corner), their count a multiple
        // of GroupSize so the light pattern has no seam. Empty when the rectangle is too small to hold a lamp.
        internal static Point[] LampPositions(double width, double height)
        {
            double w = width - 2 * Inset, h = height - 2 * Inset;
            if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h)) return Array.Empty<Point>();
            double perimeter = 2 * (w + h);
            int count = Math.Max(2 * GroupSize, GroupSize * (int)Math.Round(perimeter / (Spacing * GroupSize)));
            double step = perimeter / count;
            var points = new Point[count];
            for (int i = 0; i < count; i++)
            {
                double distance = i * step;
                Point point;
                if (distance < w) point = new Point(distance, 0);
                else if ((distance -= w) < h) point = new Point(w, distance);
                else if ((distance -= h) < w) point = new Point(w - distance, h);
                else point = new Point(0, Math.Max(0, h - (distance - w)));
                points[i] = new Point(point.X + Inset, point.Y + Inset);
            }
            return points;
        }

        // 2 = lit, 1 = glowing (just behind the lit one), 0 = dark; "phase" moves the lit lamp one lamp on.
        internal static int Level(int lamp, int phase)
        {
            int behind = (((lamp - phase) % GroupSize) + GroupSize) % GroupSize;
            return behind == 0 ? 2 : behind == GroupSize - 1 ? 1 : 0;
        }

        internal static bool AnimationsAllowed() =>
            SystemParameters.ClientAreaAnimation && !System.Windows.Forms.SystemInformation.TerminalServerSession;

        private void Sync()
        {
            if (!ShouldAnimate(IsRunning, IsVisible && IsLoaded, AnimationsAllowed()))
            {
                timer?.Stop();
                phase = 0;
                return;
            }
            if (timer == null)
            {
                timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = StepInterval };
                timer.Tick += (_, _) => { phase = (phase + 1) % GroupSize; InvalidateVisual(); };
            }
            if (!timer.IsEnabled) timer.Start();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!IsRunning || Foreground == null) return;
            if (lamps == null || layoutSize != RenderSize)
            {
                lamps = LampPositions(RenderSize.Width, RenderSize.Height);
                layoutSize = RenderSize;
            }
            for (int level = 0; level < LevelOpacity.Length; level++)
            {
                drawingContext.PushOpacity(LevelOpacity[level]);
                for (int i = 0; i < lamps.Length; i++)
                    if (Level(i, phase) == level) drawingContext.DrawEllipse(Foreground, null, lamps[i], LampRadius, LampRadius);
                drawingContext.Pop();
            }
        }
    }
}
