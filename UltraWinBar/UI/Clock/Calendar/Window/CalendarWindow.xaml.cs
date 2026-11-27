using System;
using System.Collections.Generic;
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

        // The first day of the month in the grid: a civil month, or a Hebrew one (CalendarGrid).
        private DateTime month = CalendarGrid.MonthStart(DateTime.Today, Settings.Instance.CalendarHebrewGrid);

        private static bool HebrewGrid => Settings.Instance.CalendarHebrewGrid;

        // The evening the grid was built for (the civil day after whose sunset it was), to build it again when that changed.
        private DateTime? eveningShown;

        private void RebuildIfEveningChanged()
        {
            if (HebrewDayClock.Now(DateTime.UtcNow, ClockZones.CalendarPlace()).Civil != eveningShown) Rebuild();
        }
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
                // The months before and after it too, so neither the click nor the first arrow has to make them.
                prepared.MakeAround();
                prepared.MakeAround();
                // The month's cells are laid out now too, not on the click (the height follows the content).
                prepared.Measure(new Size(prepared.Width, double.PositiveInfinity));
                prepared.Arrange(new Rect(0, 0, prepared.Width, prepared.DesiredSize.Height));
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
            if (!created) shown.RebuildIfEveningChanged();
            else if (shown.eveningShown != HebrewDayClock.Now(DateTime.UtcNow, ClockZones.CalendarPlace()).Civil) shown.Rebuild();
            shown.FillWorkArea(clock);
            if (created) shown.Show();
            if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
            // Up over the other windows even when Windows refuses the activation (a click on the panel).
            shown.Topmost = true;
            shown.Topmost = false;
            shown.Activate();
        }

        // The calendar fills the work area of the clock's screen (the screen less the panels); it is neither resized
        // nor maximized.
        private void FillWorkArea(FrameworkElement clock)
        {
            Rect area = SystemParameters.WorkArea; // no clock: the main screen's
            if (clock != null && PresentationSource.FromVisual(clock) != null) area = WorkAreaOf(clock);
            MinWidth = MinHeight = 0;
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;
        }

        private static Rect WorkAreaOf(FrameworkElement clock)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(clock);
            Point topLeft = clock.PointToScreen(new Point(0, 0));
            System.Drawing.Rectangle workPixels = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)topLeft.X, (int)topLeft.Y)).WorkingArea;
            return new Rect(workPixels.Left / dpi.DpiScaleX, workPixels.Top / dpi.DpiScaleY, workPixels.Width / dpi.DpiScaleX, workPixels.Height / dpi.DpiScaleY);
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

        // The grid of the month shown, and the months of a year before and after it made ahead in hidden lists of their
        // own, so the arrows only bring a list into sight: making a month's cells (their templates and layout) is what
        // takes the time. As the months go by, the lists that fall out of the year are filled with the ones that come in.
        private const int MonthsAround = 12;
        private ListBox current;
        private readonly Dictionary<DateTime, (ListBox List, string Stamp)> made = new();
        private readonly Stack<ListBox> spare = new();
        private int settingsVersion;

        // What a made month depends on besides its start: the kind of grid, the day, the evening and the settings shown.
        private string Stamp(HebrewDayNow now) => string.Join("|", HebrewGrid, DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            now.Civil?.ToString("yyyyMMdd", CultureInfo.InvariantCulture), settingsVersion);

        // A hidden list like the one of the window, in the same place.
        private ListBox NewMonthList()
        {
            var list = new ListBox
            {
                SelectionMode = MonthList.SelectionMode,
                Style = MonthList.Style,
                ItemsPanel = MonthList.ItemsPanel,
                ItemContainerStyle = MonthList.ItemContainerStyle,
                ItemTemplate = MonthList.ItemTemplate,
                Visibility = Visibility.Hidden,
                IsHitTestVisible = false,
                Focusable = false,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.SelectionChanged += MonthList_SelectionChanged;
            list.PreviewKeyDown += MonthList_PreviewKeyDown;
            ((Panel)MonthList.Parent).Children.Add(list);
            return list;
        }

        // The cells of a month into a list. From sunset to midnight the Hebrew day has turned already: the civil day that
        // is still on and the one whose Hebrew date has begun share a shade (two half cells that make a parallelogram).
        // Five weeks, six only for a month that reaches into a sixth.
        private void Fill(ListBox list, DateTime start, HebrewDayNow now)
        {
            HebrewPlace place = ClockZones.CalendarPlace();
            bool israel = DayDetailsBuilder.IsLandOfIsrael(place);
            bool hebrew = HebrewGrid;
            bool was = rebuilding;
            rebuilding = true;
            try
            {
                list.ItemsSource = CalendarGrid.Days(start, hebrew)
                    .Select(day => DayDetailsBuilder.Cell(day, start, DateTime.Today, israel, AppLanguage, Settings.Instance.HiddenYahrzeits, Text, place))
                    .Select(cell => cell with
                    {
                        OutsideMonth = !CalendarGrid.InMonth(cell.Date, start, hebrew),
                        EveningEnds = now.Civil == cell.Date,
                        EveningBegins = now.AfterSunset && now.Day == cell.Date,
                    }).ToList();
            }
            finally
            {
                rebuilding = was;
            }
        }

        // The month into the list it had, or a free one; it is then made for the stamp.
        private ListBox Make(DateTime start, HebrewDayNow now, string stamp)
        {
            ListBox list = made.TryGetValue(start, out var had) ? had.List : spare.Count > 0 ? spare.Pop() : NewMonthList();
            Fill(list, start, now);
            made[start] = (list, stamp);
            return list;
        }

        // A list no month needs: emptied, kept for the next.
        private void Free(ListBox list)
        {
            list.ItemsSource = null;
            spare.Push(list);
        }

        // The month with its cells and the details of the chosen day, from the settings as they are now; the months
        // around it are made again when the program is idle.
        private void Rebuild()
        {
            if (current == null)
            {
                current = MonthList;
                // Shown, the calendar makes the whole year around; before that only the months next to it.
                IsVisibleChanged += (_, _) =>
                {
                    if (IsVisible) Dispatcher.BeginInvoke(new Action(MakeAround), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
            }
            settingsVersion++;
            WeekdayHeader.Children.Clear();
            // The days of the week as the Jewish count has them: 1 (Sunday) to 6 (Friday), then Shabbat; no names of a language.
            foreach (string name in WeekdayHeaders(Text("calendar_shabbat")))
                WeekdayHeader.Children.Add(new TextBlock { Text = name, TextAlignment = TextAlignment.Center, FontSize = 14, FontWeight = FontWeights.Bold, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 4) });
            GridSwitch.Content = Text(HebrewGrid ? "calendar_grid_civil" : "calendar_grid_hebrew");
            HebrewDayNow now = HebrewDayClock.Now(DateTime.UtcNow, ClockZones.CalendarPlace());
            eveningShown = now.Civil;
            // The list in sight keeps showing the month (the kind of grid may have changed what that is).
            foreach (var pair in made.Where(pair => pair.Value.List == current).ToList()) made.Remove(pair.Key);
            if (made.TryGetValue(month, out var other)) Free(other.List);
            made[month] = (current, null);
            current = Make(month, now, Stamp(now));
            Present();
        }

        // The list of the month in sight on the chosen day, with its details; the others hidden, made ahead when idle.
        private void Present()
        {
            rebuilding = true;
            try
            {
                foreach (ListBox list in made.Values.Select(entry => entry.List).Concat(spare))
                {
                    bool mine = list == current;
                    list.Visibility = mine ? Visibility.Visible : Visibility.Hidden;
                    list.IsHitTestVisible = list.Focusable = mine;
                }
                var cells = current.Items.OfType<DayCell>().ToList();
                current.SelectedItem = cells.FirstOrDefault(cell => cell.Date == chosen.Date) ?? cells.First(cell => cell.Date == month);
            }
            finally
            {
                rebuilding = false;
            }
            ShowDetails();
            Dispatcher.BeginInvoke(new Action(MakeAround), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // The months around the one in sight, nearest first: the next, the previous, the one after the next and so on.
        // A calendar not shown yet makes only the two next to it: hidden lists are laid out when it is shown.
        private IEnumerable<DateTime> MonthsNear()
        {
            int reach = IsVisible ? MonthsAround : 1;
            DateTime next = month, previous = month;
            for (int i = 0; i < reach; i++)
            {
                yield return next = CalendarGrid.Next(next, HebrewGrid);
                yield return previous = CalendarGrid.Previous(previous, HebrewGrid);
            }
        }

        // One month around the one in sight, when the program is idle, then the next: those out of the year free their lists.
        private void MakeAround()
        {
            HebrewDayNow now = HebrewDayClock.Now(DateTime.UtcNow, ClockZones.CalendarPlace());
            string stamp = Stamp(now);
            var near = MonthsNear().ToList();
            foreach (var pair in made.Where(pair => pair.Value.List != current && !near.Contains(pair.Key)).ToList())
            {
                made.Remove(pair.Key);
                Free(pair.Value.List);
            }
            foreach (DateTime start in near)
            {
                if (made.TryGetValue(start, out var had) && had.Stamp == stamp) continue;
                Make(start, now, stamp);
                Dispatcher.BeginInvoke(new Action(MakeAround), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return;
            }
        }

        private void ShowMonth(DateTime anyDay, DateTime day)
        {
            DateTime start = CalendarGrid.MonthStart(anyDay, HebrewGrid);
            chosen = day.Date;
            if (start != month)
            {
                // A month made ahead is only brought into sight; another is made into a free list.
                HebrewDayNow now = HebrewDayClock.Now(DateTime.UtcNow, ClockZones.CalendarPlace());
                string stamp = Stamp(now);
                current = made.TryGetValue(start, out var had) && had.Stamp == stamp ? had.List : Make(start, now, stamp);
                month = start;
            }
            Present();
        }

        private void Previous_Click(object sender, RoutedEventArgs e) => ShowOther(CalendarGrid.Previous(month, HebrewGrid));

        private void Next_Click(object sender, RoutedEventArgs e) => ShowOther(CalendarGrid.Next(month, HebrewGrid));

        // Another month, on the chosen day's number there.
        private void ShowOther(DateTime start) => ShowMonth(start, CalendarGrid.SameDay(chosen, start, HebrewGrid));

        // The grid goes from civil months to Hebrew ones and back; it stays on the chosen day.
        private void GridSwitch_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.CalendarHebrewGrid = !Settings.Instance.CalendarHebrewGrid;
            month = CalendarGrid.MonthStart(chosen, HebrewGrid);
            Rebuild();
        }

        private void Today_Click(object sender, RoutedEventArgs e) => ShowMonth(DateTime.Today, DateTime.Today);

        private void MonthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (rebuilding || sender != current || current.SelectedItem is not DayCell cell) return;
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
            if (CalendarGrid.InMonth(day, month, HebrewGrid))
            {
                current.SelectedItem = current.Items.OfType<DayCell>().First(cell => cell.Date == day.Date);
                (current.ItemContainerGenerator.ContainerFromItem(current.SelectedItem) as ListBoxItem)?.Focus();
            }
            else
            {
                ShowMonth(day, day);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.PageUp) ShowOther(CalendarGrid.Previous(month, HebrewGrid));
            else if (e.Key == Key.PageDown) ShowOther(CalendarGrid.Next(month, HebrewGrid));
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
            // The details as cards: the day (its special days as colored blocks, the start and end of the rest), the
            // yahrzeits, the times of the day, the molad.
            var day = DayDetailsBuilder.Cell(chosen, chosen, DateTime.Today, DayDetailsBuilder.IsLandOfIsrael(place), AppLanguage, Settings.Instance.HiddenYahrzeits, Text, place);
            if (day.Events.Count > 0 || details.Rest != null)
            {
                Panel card = Card(null);
                foreach (CellEvent special in day.Events) card.Children.Add(Chip(special));
                // The day of rest: when it begins and ends, one highlighted line with candles.
                if (details.Rest != null) card.Children.Add(RestLine(details.Rest, null));
            }
            // The counting of the Omer, a card of its own: the day, and from the seventh the weeks and days.
            int omer = JewishCalendar.OmerDay(chosen);
            if (omer > 0)
            {
                // The heading after the tree of the sefirot; the day, its weeks and days, and its sefira.
                var heading = new StackPanel { Orientation = Orientation.Horizontal };
                heading.Children.Add(new Viewbox { Height = 22, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = new ContentControl { Focusable = false, Content = FindResource("OmerIcon") } });
                heading.Children.Add(Heading(Text("calendar_omer")));
                Panel card = Card(heading);
                Line(card, string.Format(Text("calendar_omer_day"), omer), bold: true);
                if (omer >= 7) Line(card, string.Format(Text("calendar_omer_weeks"), omer / 7, omer % 7), opacity: 0.85);
                Line(card, JewishCalendar.OmerSefira(omer, AppLanguage), opacity: 0.85);
            }
            // An eclipse, a card of its own: its Hebrew name after its sign, then what it is and when.
            foreach (EclipseLine eclipse in details.Eclipses ?? Array.Empty<EclipseLine>())
            {
                var heading = new StackPanel { Orientation = Orientation.Horizontal };
                heading.Children.Add(new Viewbox { Height = 22, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new ContentControl { Focusable = false, Content = FindResource(eclipse.Solar ? "SolarEclipseIcon" : "LunarEclipseIcon") } });
                heading.Children.Add(Heading(eclipse.Name));
                Line(Card(heading), eclipse.Text, opacity: 0.85);
            }
            if (details.Yahrzeits.Count > 0)
            {
                Panel card = Card(Heading(Text("calendar_yahrzeits")));
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
                    row.Children.Add(NameActions(name, row));
                    row.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                    card.Children.Add(row);
                }
            }
            {
                Panel card = Card(TimesHeading(details.Place, details.Chanukah));
                if (details.Place == null)
                {
                    Line(card, Text("calendar_no_place"), opacity: 0.8);
                }
                else
                {
                    foreach (TimeLine time in details.Times)
                    {
                        // The candles and the havdalah of a day of rest, and the candles of Hanukkah, stand out among the times.
                        if (time.Rest || time.Chanukah)
                        {
                            card.Children.Add(RestLine(time.Name, time.Time, time.Chanukah));
                            continue;
                        }
                        // The whole row lights up under the pointer (the style takes the colors of the theme).
                        var row = new DockPanel();
                        var when = new TextBlock { Text = time.Time, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight };
                        DockPanel.SetDock(when, Dock.Right);
                        row.Children.Add(when);
                        row.Children.Add(new TextBlock { Text = time.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                        card.Children.Add(new Border { Style = (Style)FindResource("DetailRow"), Child = row });
                    }
                }
            }
            if (details.Molad != null) Line(Card(null), details.Molad, opacity: 0.8);
        }

        // A line of the day of rest: the accent's soft fill with its edge, two candles (or a hanukkiah), the text in bold and the
        // time at the right.
        private FrameworkElement RestLine(string text, string time, bool hanukkiah = false)
        {
            var row = new DockPanel();
            var candles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            if (hanukkiah) candles.Children.Add(new Viewbox { Height = 18, Child = new ContentControl { Focusable = false, Content = FindResource("ChanukiahIcon") } });
            for (int i = 0; i < (hanukkiah ? 0 : 2); i++)
            {
                var candle = (FrameworkElement)FindResource("YahrzeitCandleIcon");
                candle.LayoutTransform = new ScaleTransform(0.7, 0.7);
                candles.Children.Add(candle);
            }
            DockPanel.SetDock(candles, Dock.Left);
            row.Children.Add(candles);
            if (time != null)
            {
                var when = new TextBlock { Text = time, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(when, Dock.Right);
                row.Children.Add(when);
            }
            row.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            return new Border
            {
                Child = row,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 2, 10, 2),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Background = (Brush)(TryFindResource("ModernAccentSoft") ?? Brushes.Transparent),
                BorderBrush = (Brush)(TryFindResource("ModernAccent") ?? SystemColors.HighlightBrush),
            };
        }

        // A card of the details: glass with a rounded border, an optional heading, and the panel its lines go into.
        private Panel Card(FrameworkElement heading)
        {
            var body = new StackPanel();
            if (heading != null)
            {
                heading.Margin = new Thickness(0, 0, 0, 6);
                body.Children.Add(heading);
            }
            Details.Children.Add(new Border
            {
                Child = body,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = (Brush)(TryFindResource("ModernGlass") ?? Brushes.Transparent),
                BorderBrush = (Brush)(TryFindResource("ModernGlassBorder") ?? SystemColors.ActiveBorderBrush),
            });
            return body;
        }

        private TextBlock Heading(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)(TryFindResource("ModernTextStrong") ?? SystemColors.WindowTextBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // A special day as the cells show it: a block with a colored edge by its kind.
        internal static (Color Back, Color Edge) ChipColors(DayKind kind) => kind switch
        {
            DayKind.YomTov => (Color.FromArgb(0x38, 0xFB, 0x71, 0x85), Color.FromArgb(0xCC, 0xFB, 0x71, 0x85)),
            DayKind.CholHamoed => (Color.FromArgb(0x26, 0xFB, 0x71, 0x85), Color.FromArgb(0x99, 0xFB, 0x71, 0x85)),
            DayKind.Fast => (Color.FromArgb(0x26, 0x94, 0xA3, 0xB8), Color.FromArgb(0x99, 0x94, 0xA3, 0xB8)),
            DayKind.Minor => (Color.FromArgb(0x2E, 0xFB, 0xBF, 0x24), Color.FromArgb(0xB3, 0xFB, 0xBF, 0x24)),
            _ => (Color.FromArgb(0x2E, 0x38, 0xBD, 0xF8), Color.FromArgb(0x99, 0x38, 0xBD, 0xF8)),
        };

        private static Border Chip(CellEvent special)
        {
            var (back, edge) = ChipColors(special.Kind);
            return new Border
            {
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Background = new SolidColorBrush(back),
                BorderBrush = new SolidColorBrush(edge),
                Child = new TextBlock
                {
                    Text = special.Name,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = special.Kind == DayKind.YomTov ? FontWeights.Bold : FontWeights.SemiBold,
                },
            };
        }

        // Two buttons at the right of a name, shown while the pointer is on its row: copy the name, and look it up with
        // Google in the browser of the system.
        private FrameworkElement NameActions(string name, Panel row)
        {
            var copy = new Button { Style = (Style)FindResource("RowIconButton"), Content = new TextBlock { Text = "\uE8C8", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 }, ToolTip = Text("calendar_copy_name") };
            copy.Click += (_, _) => CopyName(name);
            var search = new Button { Style = (Style)FindResource("RowIconButton"), Content = FindResource("ChromeIcon"), ToolTip = Text("calendar_search_name") };
            search.Click += (_, _) => SearchName(name);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
            actions.Children.Add(copy);
            actions.Children.Add(search);
            DockPanel.SetDock(actions, Dock.Right);
            row.Background = Brushes.Transparent;
            row.MouseEnter += (_, _) => actions.Visibility = Visibility.Visible;
            row.MouseLeave += (_, _) => actions.Visibility = Visibility.Hidden;
            return actions;
        }

        // The name without what is in brackets ("Vilna Gaon (Gra)" is looked up as "Vilna Gaon").
        internal static string SearchAddress(string name) =>
            "https://www.google.com/search?q=" + Uri.EscapeDataString(System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)", "").Trim());

        private static void CopyName(string name)
        {
            // The clipboard may be held by another program for a moment.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { Clipboard.SetText(name); return; }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(30); }
            }
        }

        private static void SearchName(string name)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SearchAddress(name)) { UseShellExecute = true })?.Dispose(); }
            catch (Exception error) { ManagedShell.Common.Logging.ShellLogger.Warning($"CalendarWindow: the browser did not open: {error.Message}"); }
        }

        // "Times of the day", with a globe at the right whose popup tells the place they are for. The popup stands above the
        // globe, right-aligned to it (so over the calendar, not beyond its edge); it shows while the pointer is on the globe,
        // and a click pins it until the next click.
        private FrameworkElement TimesHeading(string place, int candles)
        {
            var heading = new DockPanel();
            // During Hanukkah the candles of the evening by the heading: a hanukkiah and their number.
            if (candles > 0)
            {
                var chanukah = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                chanukah.Children.Add(new Viewbox { Height = 20, Margin = new Thickness(0, 0, 4, 0), Child = new ContentControl { Focusable = false, Content = FindResource("ChanukiahIcon") } });
                chanukah.Children.Add(new TextBlock { Text = candles.ToString(CultureInfo.InvariantCulture), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                DockPanel.SetDock(chanukah, Dock.Right);
                heading.Children.Add(chanukah);
            }
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
            heading.Children.Add(Heading(Text("calendar_times")));
            return heading;
        }

        private static void Line(Panel target, string text, bool bold = false, double size = 0, double top = 0, double opacity = 1, FlowDirection? flow = null)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), Opacity = opacity };
            if (bold) block.FontWeight = FontWeights.SemiBold;
            if (size > 0) block.FontSize = size;
            if (flow.HasValue) block.FlowDirection = flow.Value;
            target.Children.Add(block);
        }
    }
}
