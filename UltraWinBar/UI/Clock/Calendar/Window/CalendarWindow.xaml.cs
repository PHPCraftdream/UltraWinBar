using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        /// <summary>Opens the calendar, or brings the open one forward: there is only one.</summary>
        public static void ShowSingle()
        {
            if (shown == null)
            {
                shown = new CalendarWindow();
                shown.Closed += (_, _) => shown = null;
                shown.Show();
            }
            if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
            shown.Activate();
        }

        public CalendarWindow()
        {
            InitializeComponent();
            ModernTheme.Track(this);
            WeakSubscriptions.SubscribeSettings(Settings_PropertyChanged);
            Closed += (_, _) => WeakSubscriptions.UnsubscribeSettings(Settings_PropertyChanged);
            Rebuild();
        }

        private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

        private static string AppLanguage => Settings.Instance.Language;

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.HebrewPlace) || e.PropertyName == nameof(Settings.HiddenYahrzeits) || e.PropertyName == nameof(Settings.Language))
                Rebuild();
        }

        // The month with its cells and the details of the chosen day, from the settings as they are now.
        private void Rebuild()
        {
            rebuilding = true;
            try
            {
                CultureInfo culture = DayDetailsBuilder.CultureOf(AppLanguage);
                MonthTitle.Text = month.ToString("Y", culture) + Environment.NewLine + DayDetailsBuilder.HebrewMonths(month, AppLanguage);
                WeekdayHeader.Children.Clear();
                foreach (string name in culture.DateTimeFormat.AbbreviatedDayNames)
                    WeekdayHeader.Children.Add(new TextBlock { Text = name, TextAlignment = TextAlignment.Center, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 2) });

                bool israel = DayDetailsBuilder.IsLandOfIsrael(Settings.Instance.HebrewPlace);
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
            HebrewPlace place = Settings.Instance.HebrewPlace;
            DayDetails details = DayDetailsBuilder.Describe(chosen, place, AppLanguage, Settings.Instance.HiddenYahrzeits, Text);
            Details.Children.Clear();
            Line(details.Heading, bold: true, size: 15);
            Line(details.HebrewDate, bold: true, size: 14, flow: FlowDirection.LeftToRight);
            if (details.Portion != null) Line(details.Portion, top: 8);
            foreach (string name in details.Events) Line(name, bold: true, top: 4);
            if (details.RestStarts != null) Line(details.RestStarts, top: 4);
            if (details.RestEnds != null) Line(details.RestEnds);
            if (details.Yahrzeits.Count > 0)
            {
                Line(Text("calendar_yahrzeits"), bold: true, top: 10);
                foreach (string name in details.Yahrzeits) Line(name);
            }
            Line(Text("calendar_times"), bold: true, top: 10);
            if (details.Place == null)
            {
                Line(Text("calendar_no_place"));
            }
            else
            {
                Line(details.Place, opacity: 0.7);
                foreach (TimeLine time in details.Times)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 0) };
                    var when = new TextBlock { Text = time.Time, Margin = new Thickness(8, 0, 0, 0), FlowDirection = FlowDirection.LeftToRight };
                    DockPanel.SetDock(when, Dock.Right);
                    row.Children.Add(when);
                    row.Children.Add(new TextBlock { Text = time.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                    Details.Children.Add(row);
                }
            }
            if (details.Molad != null) Line(details.Molad, top: 10, opacity: 0.7);
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
