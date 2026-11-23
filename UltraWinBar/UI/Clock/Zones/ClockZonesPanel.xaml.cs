using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UltraWinBar.Utilities;

namespace UltraWinBar.Controls
{
    /// <summary>
    /// The time zones of the clock in the settings: the main one (the computer's unless another is chosen), a second and a
    /// third, chosen from the zones of the place data that this computer knows. Each has a check box for its name and a
    /// place: the times of the day of the calendar are those of the place, on the clock of the zone. The place of the main
    /// zone is the place of the Hebrew date.
    /// </summary>
    public partial class ClockZonesPanel : UserControl
    {
        private readonly bool ready;
        private readonly List<ZoneChoice> zones;

        public ClockZonesPanel()
        {
            InitializeComponent();
            DataContext = Settings.Instance;

            DateTime now = DateTime.UtcNow;
            zones = PlaceDatabase.Shared.TimeZones.Where(id => ZoneClock.Find(id) != null).Select(id => new ZoneChoice(id, ZoneClock.Describe(id, now))).ToList();
            // A list for each box: the search line of one filters only its own.
            var main = new List<ZoneChoice> { new ZoneChoice("", string.Format(Text("zone_computer"), ZoneClock.LocalName())) };
            main.AddRange(zones);
            var none = new ZoneChoice("", Text("zone_none"));
            MainBox.ItemsSource = main;
            SecondBox.ItemsSource = new[] { none }.Concat(zones).ToList();
            ThirdBox.ItemsSource = new[] { none }.Concat(zones).ToList();
            Select(MainBox, Settings.Instance.MainTimeZone);
            Select(SecondBox, Settings.Instance.SecondTimeZone);
            Select(ThirdBox, Settings.Instance.ThirdTimeZone);
            ShowPlaces();

            WeakSubscriptions.SubscribeSettings(Settings_PropertyChanged);
            Unloaded += (_, _) => WeakSubscriptions.UnsubscribeSettings(Settings_PropertyChanged);
            ready = true;
        }

        private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

        // An unknown or empty id selects the first entry: the computer's zone, or none.
        private static void Select(ComboBox box, string id)
        {
            var items = (List<ZoneChoice>)box.ItemsSource;
            box.SelectedItem = items.FirstOrDefault(zone => zone.Id == id && id != "") ?? items[0];
        }

        // The place of the main zone is also set in the clock group's own row: follow it.
        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.HebrewPlace) || e.PropertyName == nameof(Settings.SecondPlace) || e.PropertyName == nameof(Settings.ThirdPlace))
                ShowPlaces();
        }

        private void ShowPlaces()
        {
            MainPlaceText.Text = Settings.Instance.HebrewPlace?.Display ?? Text("hebrew_place_none");
            SecondPlaceText.Text = Settings.Instance.SecondPlace?.Display ?? Text("hebrew_place_none");
            ThirdPlaceText.Text = Settings.Instance.ThirdPlace?.Display ?? Text("hebrew_place_none");
        }

        private static HebrewPlace PlaceOf(int zone) => ClockZones.PlaceOf(zone);

        private static void SetPlace(int zone, HebrewPlace place)
        {
            if (zone == ClockZones.Second) Settings.Instance.SecondPlace = place;
            else if (zone == ClockZones.Third) Settings.Instance.ThirdPlace = place;
            else Settings.Instance.HebrewPlace = place;
        }

        private ComboBox BoxOf(int zone) => zone == ClockZones.Second ? SecondBox : zone == ClockZones.Third ? ThirdBox : MainBox;

        private string ChosenZone(int zone) => zone == ClockZones.Second ? Settings.Instance.SecondTimeZone
            : zone == ClockZones.Third ? Settings.Instance.ThirdTimeZone : Settings.Instance.MainTimeZone;

        // The place of the zone is asked for; a second or third zone not chosen yet becomes the zone of the place (the main
        // one stays the computer's unless the user changes it).
        private void ChoosePlace(int zone)
        {
            var window = new PlaceWindow(PlaceOf(zone)) { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true || window.Result == null) return;
            SetPlace(zone, window.Result);
            string own = window.Result.TimeZoneId;
            if (zone != ClockZones.Main && string.IsNullOrEmpty(ChosenZone(zone)) && zones.Any(item => item.Id == own))
                Select(BoxOf(zone), own);
            ShowPlaces();
        }

        private void MainPlaceChoose_Click(object sender, RoutedEventArgs e) => ChoosePlace(ClockZones.Main);

        private void SecondPlaceChoose_Click(object sender, RoutedEventArgs e) => ChoosePlace(ClockZones.Second);

        private void ThirdPlaceChoose_Click(object sender, RoutedEventArgs e) => ChoosePlace(ClockZones.Third);

        private void MainPlaceClear_Click(object sender, RoutedEventArgs e) => SetPlace(ClockZones.Main, null);

        private void SecondPlaceClear_Click(object sender, RoutedEventArgs e) => SetPlace(ClockZones.Second, null);

        private void ThirdPlaceClear_Click(object sender, RoutedEventArgs e) => SetPlace(ClockZones.Third, null);

        private void MainBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready && MainBox.SelectedItem is ZoneChoice zone) Settings.Instance.MainTimeZone = zone.Id;
        }

        private void SecondBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready && SecondBox.SelectedItem is ZoneChoice zone) Settings.Instance.SecondTimeZone = zone.Id;
        }

        private void ThirdBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready && ThirdBox.SelectedItem is ZoneChoice zone) Settings.Instance.ThirdTimeZone = zone.Id;
        }
    }
}
