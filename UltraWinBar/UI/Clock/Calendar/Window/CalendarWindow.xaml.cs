using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UltraWinBar.Utilities;

namespace UltraWinBar
{
    /// <summary>
    /// The Jewish calendar, opened by a click on the Hebrew date of the clock: a month of days with their Hebrew dates and
    /// what is special about them, and for the chosen day its festivals, weekly portion, yahrzeits, molad and the times of
    /// the day at the place chosen in the settings.
    /// </summary>
    public partial class CalendarWindow : Window
    {
        private static CalendarWindow shown;

        private DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime chosen = DateTime.Today;
        private bool rebuilding;

        /// <summary>
        /// Opens the calendar, or brings the open one forward: there is only one. It stands in the corner of the clock
        /// it was opened from, on the side of the panel that faces the desktop.
        /// </summary>
        // True when a visible window of another program lies over the calendar (higher in the z-order and overlapping).
        internal static bool IsCovered(IntPtr handle)
        {
            if (!ManagedShell.Interop.NativeMethods.GetWindowRect(handle, out var mine)) return false;
            uint own = (uint)Environment.ProcessId;
            for (IntPtr above = ManagedShell.Interop.NativeMethods.GetWindow(handle, ManagedShell.Interop.NativeMethods.GetWindow_Cmd.GW_HWNDPREV);
                 above != IntPtr.Zero;
                 above = ManagedShell.Interop.NativeMethods.GetWindow(above, ManagedShell.Interop.NativeMethods.GetWindow_Cmd.GW_HWNDPREV))
            {
                if (!ManagedShell.Interop.NativeMethods.IsWindowVisible(above) || ManagedShell.Interop.NativeMethods.IsIconic(above)) continue;
                ManagedShell.Interop.NativeMethods.GetWindowThreadProcessId(above, out uint process);
                if (process == own) continue;
                if (ManagedShell.Interop.NativeMethods.DwmGetWindowAttribute(above, ManagedShell.Interop.NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, out uint cloaked, sizeof(uint)) == 0 && cloaked != 0) continue;
                if (!ManagedShell.Interop.NativeMethods.GetWindowRect(above, out var other)) continue;
                if (other.Left < mine.Right && other.Right > mine.Left && other.Top < mine.Bottom && other.Bottom > mine.Top) return true;
            }
            return false;
        }

        // A calendar made ahead (on a hover over the clock, and once when the program is idle after its start), so a click
        // only has to show it: making one (its markup, the month's cells, the times of the day) is what took the time.
        private static CalendarWindow prepared;

        public static void Prepare()
        {
            if (shown != null || (prepared != null && prepared.madeOn == DateTime.Today)) return;
            prepared?.Close();
            try
            {
                prepared = new CalendarWindow();
                // The month's cells are laid out now too, not on the click.
                prepared.Measure(new Size(prepared.Width, prepared.Height));
                prepared.Arrange(new Rect(0, 0, prepared.Width, prepared.Height));
            }
            catch (Exception error)
            {
                ManagedShell.Common.Logging.ShellLogger.Debug($"CalendarWindow: not prepared: {error.Message}");
                prepared = null;
            }
        }

        private readonly DateTime madeOn = DateTime.Today;

        public static void ShowSingle(FrameworkElement clock = null, bool toggle = true)
        {
            if (shown != null)
            {
                IntPtr handle = new WindowInteropHelper(shown).Handle;
                bool here = VirtualDesktopContext.Instance?.IsWindowOnCurrentDesktop(handle) ?? true;
                // A second click on the date closes the calendar that is open and in sight on this desktop; one under
                // other windows is brought up over them instead.
                if (clock != null && toggle && here && shown.IsVisible && shown.WindowState != WindowState.Minimized && !IsCovered(handle))
                {
                    shown.Close();
                    return;
                }

                // One open on another desktop comes to this one: the user is never taken to it. When it cannot be moved,
                // it is closed and opened anew here.
                if (!here && VirtualDesktopContext.Instance?.BringWindowToCurrentDesktop(handle) != true)
                {
                    shown.Close();
                }
            }

            bool created = shown == null;
            if (created)
            {
                if (prepared != null && prepared.madeOn != DateTime.Today)
                {
                    prepared.Close();
                    prepared = null;
                }
                shown = prepared ?? new CalendarWindow();
                prepared = null;
                shown.Closed += (_, _) => shown = null;
            }
            if (clock != null) shown.PlaceAtCorner(clock);
            if (created) shown.Show();
            if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
            // Up over the other windows even when Windows refuses the activation (a click on the panel).
            shown.Topmost = true;
            shown.Topmost = false;
            shown.Activate();
        }

        // The corner of the calendar that is nearest to the clock meets the clock's corner: above a bottom panel,
        // under a top one, beside a side one, always inside the work area of the clock's screen.
        private void PlaceAtCorner(FrameworkElement clock)
        {
            if (PresentationSource.FromVisual(clock) == null) return;
            DpiScale dpi = VisualTreeHelper.GetDpi(clock);
            Point topLeft = clock.PointToScreen(new Point(0, 0));
            Point bottomRight = clock.PointToScreen(new Point(clock.ActualWidth, clock.ActualHeight));
            System.Drawing.Rectangle workPixels = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)topLeft.X, (int)topLeft.Y)).WorkingArea;
            Rect area = new Rect(workPixels.Left / dpi.DpiScaleX, workPixels.Top / dpi.DpiScaleY, workPixels.Width / dpi.DpiScaleX, workPixels.Height / dpi.DpiScaleY);
            Rect at = new Rect(new Point(topLeft.X / dpi.DpiScaleX, topLeft.Y / dpi.DpiScaleY), new Point(bottomRight.X / dpi.DpiScaleX, bottomRight.Y / dpi.DpiScaleY));

            const double gap = 8;
            double width = Math.Min(Width, area.Width - 2 * gap), height = Math.Min(Height, area.Height - 2 * gap);
            Width = width;
            Height = height;
            ManagedShell.AppBar.AppBarEdge edge = (GetWindow(clock) as ManagedShell.AppBar.AppBarWindow)?.AppBarEdge ?? ManagedShell.AppBar.AppBarEdge.Bottom;
            bool lowerHalf = (at.Top + at.Bottom) / 2 > (area.Top + area.Bottom) / 2;
            double left, top;
            switch (edge)
            {
                case ManagedShell.AppBar.AppBarEdge.Left:
                    left = area.Left + gap;
                    top = lowerHalf ? at.Bottom - height : at.Top;
                    break;
                case ManagedShell.AppBar.AppBarEdge.Right:
                    left = area.Right - width - gap;
                    top = lowerHalf ? at.Bottom - height : at.Top;
                    break;
                case ManagedShell.AppBar.AppBarEdge.Top:
                    left = at.Right - width;
                    top = area.Top + gap;
                    break;
                default:
                    left = at.Right - width;
                    top = area.Bottom - height - gap;
                    break;
            }
            Left = Math.Max(area.Left + gap, Math.Min(left, area.Right - width - gap));
            Top = Math.Max(area.Top + gap, Math.Min(top, area.Bottom - height - gap));
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public CalendarWindow()
        {
            InitializeComponent();
            ModernTheme.Track(this);
            WeakSubscriptions.SubscribeSettings(Settings_PropertyChanged);
            Closed += (_, _) => WeakSubscriptions.UnsubscribeSettings(Settings_PropertyChanged);
            Rebuild();
        }

        internal static string[] WeekdayHeaders(string shabbat) => new[] { "1", "2", "3", "4", "5", "6", shabbat };

        private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

        private static string AppLanguage => Settings.Instance.Language;

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.HebrewPlace) || e.PropertyName == nameof(Settings.HiddenYahrzeits) || e.PropertyName == nameof(Settings.Language) ||
                e.PropertyName == nameof(Settings.CalendarZone) || e.PropertyName == nameof(Settings.SecondTimeZone) || e.PropertyName == nameof(Settings.ThirdTimeZone) ||
                e.PropertyName == nameof(Settings.SecondPlace) || e.PropertyName == nameof(Settings.ThirdPlace))
                Rebuild();
        }

        // The month with its cells and the details of the chosen day, from the settings as they are now.
        private void Rebuild()
        {
            rebuilding = true;
            try
            {
                WeekdayHeader.Children.Clear();
                // The days of the week as the Jewish count has them: 1 (Sunday) to 6 (Friday), then Shabbat; no names of a language.
                foreach (string name in WeekdayHeaders(Text("calendar_shabbat")))
                    WeekdayHeader.Children.Add(new TextBlock { Text = name, TextAlignment = TextAlignment.Center, FontSize = 14, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });

                bool israel = DayDetailsBuilder.IsLandOfIsrael(ClockZones.CalendarPlace());
                DateTime start = month.AddDays(-(int)month.DayOfWeek);
                var cells = Enumerable.Range(0, 42)
                    .Select(i => DayDetailsBuilder.Cell(start.AddDays(i), month, DateTime.Today, israel, AppLanguage, Settings.Instance.HiddenYahrzeits, Text)).ToList();
                MonthList.ItemsSource = cells;
                MonthList.SelectedItem = cells.FirstOrDefault(cell => cell.Date == chosen.Date) ?? cells.First(cell => cell.Date == month);
            }
            finally
            {
                rebuilding = false;
            }
            ShowDetails();
        }

        private void ShowMonth(DateTime anyDay, DateTime day)
        {
            month = new DateTime(anyDay.Year, anyDay.Month, 1);
            chosen = day.Date;
            Rebuild();
        }

        private void Previous_Click(object sender, RoutedEventArgs e) => ShowMonth(month.AddMonths(-1), SameDayOf(month.AddMonths(-1)));

        private void Next_Click(object sender, RoutedEventArgs e) => ShowMonth(month.AddMonths(1), SameDayOf(month.AddMonths(1)));

        private void Today_Click(object sender, RoutedEventArgs e) => ShowMonth(DateTime.Today, DateTime.Today);

        // The chosen day's number in another month, or its last day when that month is shorter.
        private DateTime SameDayOf(DateTime otherMonth) =>
            new DateTime(otherMonth.Year, otherMonth.Month, Math.Min(chosen.Day, DateTime.DaysInMonth(otherMonth.Year, otherMonth.Month)));

        private void MonthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (rebuilding || MonthList.SelectedItem is not DayCell cell) return;
            chosen = cell.Date;
            ShowDetails();
        }

        // The arrow keys go by the days of the week: left and right a day, up and down a week.
        private void MonthList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int step = e.Key == Key.Up ? -7 : e.Key == Key.Down ? 7 : e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
            if (step == 0) return;
            if (FlowDirection == FlowDirection.RightToLeft && (e.Key == Key.Left || e.Key == Key.Right)) step = -step;
            e.Handled = true;
            DateTime day = chosen.AddDays(step);
            if (day.Year == month.Year && day.Month == month.Month)
            {
                MonthList.SelectedItem = MonthList.Items.OfType<DayCell>().First(cell => cell.Date == day.Date);
                (MonthList.ItemContainerGenerator.ContainerFromItem(MonthList.SelectedItem) as ListBoxItem)?.Focus();
            }
            else
            {
                ShowMonth(day, day);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.PageUp) ShowMonth(month.AddMonths(-1), SameDayOf(month.AddMonths(-1)));
            else if (e.Key == Key.PageDown) ShowMonth(month.AddMonths(1), SameDayOf(month.AddMonths(1)));
            else return;
            e.Handled = true;
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => new CalendarSettingsWindow { Owner = this }.ShowDialog();

        private void ShowDetails()
        {
            HebrewPlace place = ClockZones.CalendarPlace();
            DayDetails details = DayDetailsBuilder.Describe(chosen, place, AppLanguage, Settings.Instance.HiddenYahrzeits, Text);
            Details.Children.Clear();
            // The chosen day's dates are in its cell (and its tooltip); its week's portion is above the month.
            // The chosen day over the month: its civil date at the left, its Hebrew date at the right, its week's portion between.
            MonthTitle.Text = chosen.ToString("d MMMM yyyy", DayDetailsBuilder.CultureOf(AppLanguage));
            HebrewMonthTitle.Text = details.HebrewDate;
            PortionTitle.Text = details.Portion ?? "";
            foreach (string name in details.Events) Line(name, bold: true, top: 4);
            if (details.RestStarts != null) Line(details.RestStarts, top: 4);
            if (details.RestEnds != null) Line(details.RestEnds);
            if (details.Yahrzeits.Count > 0)
            {
                Line(Text("calendar_yahrzeits"), bold: true, top: 10);
                // Each name after a memorial candle; the eldest first (the list comes in order of birth).
                foreach (string name in details.Yahrzeits)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                    var candle = (FrameworkElement)FindResource("YahrzeitCandleIcon");
                    candle.LayoutTransform = new ScaleTransform(0.8, 0.8);
                    candle.Margin = new Thickness(0, 0, 6, 0);
                    candle.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(candle, Dock.Left);
                    row.Children.Add(candle);
                    row.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                    Details.Children.Add(row);
                }
            }
            TimesHeading(details.Place);
            if (details.Place == null)
            {
                Line(Text("calendar_no_place"));
            }
            else
            {
                foreach (TimeLine time in details.Times)
                {
                    // The whole row lights up under the pointer (the style takes the colors of the theme).
                    var row = new DockPanel();
                    var when = new TextBlock { Text = time.Time, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight };
                    DockPanel.SetDock(when, Dock.Right);
                    row.Children.Add(when);
                    row.Children.Add(new TextBlock { Text = time.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                    Details.Children.Add(new Border { Style = (Style)FindResource("DetailRow"), Child = row });
                }
            }
            if (details.Molad != null) Line(details.Molad, top: 10, opacity: 0.7);
        }

        // "Times of the day", with a globe at the right whose popup tells the place they are for. The popup stands above the
        // globe, right-aligned to it (so over the calendar, not beyond its edge); it shows while the pointer is on the globe,
        // and a click pins it until the next click.
        private void TimesHeading(string place)
        {
            var heading = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (place != null)
            {
                var text = new TextBlock { Text = place, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(10, 6, 10, 6) };
                var popup = new System.Windows.Controls.Primitives.Popup
                {
                    StaysOpen = true,
                    AllowsTransparency = true,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
                    Child = new Border
                    {
                        Child = text,
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        Background = (Brush)(TryFindResource("ModernBase") ?? SystemColors.WindowBrush),
                        BorderBrush = (Brush)(TryFindResource("ModernGlassBorderStrong") ?? SystemColors.ActiveBorderBrush),
                    },
                };
                popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, -popupSize.Height - 4),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
                };
                var globe = new Button
                {
                    Content = "\uE774",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                popup.PlacementTarget = globe;
                bool pinned = false;
                globe.MouseEnter += (_, _) => popup.IsOpen = true;
                globe.MouseLeave += (_, _) => { if (!pinned) popup.IsOpen = false; };
                globe.Click += (_, _) =>
                {
                    pinned = !pinned;
                    popup.IsOpen = pinned;
                };
                EventHandler away = (_, _) => { pinned = false; popup.IsOpen = false; };
                Deactivated += away;
                globe.Unloaded += (_, _) => { Deactivated -= away; popup.IsOpen = false; };
                DockPanel.SetDock(globe, Dock.Right);
                heading.Children.Add(globe);
                heading.Children.Add(popup);
            }
            heading.Children.Add(new TextBlock { Text = Text("calendar_times"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Details.Children.Add(heading);
        }

        private void Line(string text, bool bold = false, double size = 0, double top = 0, double opacity = 1, FlowDirection? flow = null)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), Opacity = opacity };
            if (bold) block.FontWeight = FontWeights.SemiBold;
            if (size > 0) block.FontSize = size;
            if (flow.HasValue) block.FlowDirection = flow.Value;
            Details.Children.Add(block);
        }
    }
}
