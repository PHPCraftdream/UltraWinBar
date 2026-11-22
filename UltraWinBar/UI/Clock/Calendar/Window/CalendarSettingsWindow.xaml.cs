using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UltraWinBar.Utilities;

namespace UltraWinBar
{
    /// <summary>
    /// The settings of the calendar: the place (and its time zone) the day is counted and the times are given for, the time zone
    /// of the second time under the clock's own, and which yahrzeits are shown, by category and one by one.
    /// </summary>
    public partial class CalendarSettingsWindow : Window
    {
        private readonly bool ready;
        private readonly YahrzeitNode everyone;

        public CalendarSettingsWindow()
        {
            InitializeComponent();
            ShowPlace();

            DateTime now = DateTime.UtcNow;
            var zones = new List<ZoneChoice> { new ZoneChoice("", Text("calendar_zone_none")) };
            zones.AddRange(PlaceDatabase.Shared.TimeZones.Where(id => ZoneClock.Find(id) != null).Select(id => new ZoneChoice(id, ZoneClock.Describe(id, now))));
            ZoneBox.ItemsSource = zones;
            ZoneBox.SelectedItem = zones.FirstOrDefault(zone => zone.Id == Settings.Instance.SecondTimeZone) ?? zones[0];

            everyone = YahrzeitNode.Build(Settings.Instance.Language, Text("calendar_all_figures"), Settings.Instance.HiddenYahrzeits);
            everyone.FigureChanged += (_, _) => Settings.Instance.HiddenYahrzeits = everyone.UncheckedKeys().ToList();
            YahrzeitTree.ItemsSource = new[] { everyone };
            ready = true;
        }

        private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

        private void ShowPlace() => PlaceText.Text = Settings.Instance.HebrewPlace?.Display ?? Text("hebrew_place_none");

        private void PlaceChoose_Click(object sender, RoutedEventArgs e)
        {
            var window = new PlaceWindow(Settings.Instance.HebrewPlace) { Owner = this };
            if (window.ShowDialog() == true && window.Result != null)
            {
                Settings.Instance.HebrewPlace = window.Result;
                ShowPlace();
            }
        }

        private void PlaceClear_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.HebrewPlace = null;
            ShowPlace();
        }

        private void Zone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready && ZoneBox.SelectedItem is ZoneChoice zone) Settings.Instance.SecondTimeZone = zone.Id;
        }
    }
}
