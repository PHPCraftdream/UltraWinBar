using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UltraWinBar.Utilities;

namespace UltraWinBar
{
    /// <summary>One of the clock's time zones that the calendar can go by: 0 main, 1 second, 2 third.</summary>
    public sealed record CalendarZoneChoice(int Zone, string Display);

    /// <summary>
    /// The settings of the calendar: the place of the main time zone, which of the clock's three time zones (each with its
    /// place) the date, the change of the day and the times of the day go by, and which yahrzeits are shown, by category
    /// and one by one.
    /// </summary>
    public partial class CalendarSettingsWindow : Window
    {
        private readonly bool ready;
        private readonly YahrzeitNode everyone;

        public CalendarSettingsWindow()
        {
            InitializeComponent();
            ModernTheme.Track(this);
            ShowPlace();

            // The zones the clock has: the computer's own, and the second and the third when they are chosen.
            DateTime now = DateTime.UtcNow;
            // Each with its place: the zone's clock and the sunset and times of its place.
            string PlaceOf(int zone) => " — " + (ClockZones.PlaceOf(zone)?.Display ?? Text("hebrew_place_none"));
            var zones = new List<CalendarZoneChoice> { new CalendarZoneChoice(ClockZones.Main, (ClockZones.IdOf(ClockZones.Main) == ""
                ? string.Format(Text("calendar_zone_main"), ZoneClock.LocalName()) : ZoneClock.Describe(ClockZones.IdOf(ClockZones.Main), now)) + PlaceOf(ClockZones.Main)) };
            foreach (int zone in new[] { ClockZones.Second, ClockZones.Third })
                if (ClockZones.IsSet(zone)) zones.Add(new CalendarZoneChoice(zone, ZoneClock.Describe(ClockZones.IdOf(zone), now) + PlaceOf(zone)));
            ZoneBox.ItemsSource = zones;
            ZoneBox.SelectedItem = zones.FirstOrDefault(zone => zone.Zone == ClockZones.CalendarZone()) ?? zones[0];
            ShowPlace();

            everyone = YahrzeitNode.Build(Settings.Instance.Language, Text("calendar_all_figures"), Settings.Instance.HiddenYahrzeits);
            everyone.FigureChanged += (_, _) => Settings.Instance.HiddenYahrzeits = everyone.UncheckedKeys().ToList();
            YahrzeitTree.ItemsSource = new[] { everyone };
            ready = true;
        }

        private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

        // The place shown and set here is the place of the zone the calendar goes by (it was always the main zone's, so a
        // calendar going by the second or third zone stayed without times of the day).
        private int ChosenZone => ZoneBox?.SelectedItem is CalendarZoneChoice zone ? zone.Zone : ClockZones.CalendarZone();

        private void ShowPlace() => PlaceText.Text = ClockZones.PlaceOf(ChosenZone)?.Display ?? Text("hebrew_place_none");

        private void PlaceChoose_Click(object sender, RoutedEventArgs e)
        {
            var window = new PlaceWindow(ClockZones.PlaceOf(ChosenZone)) { Owner = this };
            if (window.ShowDialog() == true && window.Result != null)
            {
                ClockZones.SetPlace(ChosenZone, window.Result);
                ShowPlace();
            }
        }

        private void PlaceClear_Click(object sender, RoutedEventArgs e)
        {
            ClockZones.SetPlace(ChosenZone, null);
            ShowPlace();
        }

        private void Zone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready && ZoneBox.SelectedItem is CalendarZoneChoice zone) Settings.Instance.CalendarZone = zone.Zone;
            ShowPlace();
        }
    }
}
