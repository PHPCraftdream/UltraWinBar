using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UltraWinBar.Utilities;

namespace UltraWinBar
{
    /// <summary>A time zone in the list of the window: its IANA id and how it reads now ("Asia/Jerusalem (UTC+02:00)").</summary>
    public sealed record ZoneChoice(string Id, string Display);

    /// <summary>
    /// Chooses the place the Hebrew date and the halachic times are kept for, and its time zone: a city found by its
    /// English name, or coordinates typed in with their country. A city brings its time zone along and typed-in
    /// coordinates the zone of the nearest city of the country; the user may change it, and must have one chosen.
    /// </summary>
    public partial class PlaceWindow : Window
    {
        private const int CityTab = 0, CoordinatesTab = 1;
        private const int MostCities = 60;

        private readonly PlaceDatabase places = PlaceDatabase.Shared;
        private readonly bool ready;
        private CoordinateParseResult coordinates;
        private string suggestedZone = "";

        /// <summary>What the user chose; set when the window closes with OK.</summary>
        public HebrewPlace Result { get; private set; }

        public PlaceWindow(HebrewPlace current)
        {
            InitializeComponent();
            ModernTheme.Track(this);
            CountryBox.ItemsSource = places.Countries;
            DateTime now = DateTime.UtcNow;
            ZoneBox.ItemsSource = places.TimeZones.Where(id => ZoneClock.Find(id) != null)
                .Select(id => new ZoneChoice(id, ZoneClock.Describe(id, now))).ToArray();
            if (current != null) Restore(current);
            ready = true;
            UpdateState();
        }

        private void Restore(HebrewPlace current)
        {
            if (current.IsCustom)
            {
                Tabs.SelectedIndex = CoordinatesTab;
                CountryBox.SelectedItem = places.FindCountry(current.CountryCode);
                CoordinatesBox.Text = CoordinateParser.Format(current.Latitude, current.Longitude);
                suggestedZone = places.NearestTimeZone(current.CountryCode, current.Latitude, current.Longitude);
                SelectZone(!string.IsNullOrEmpty(current.TimeZoneId) ? current.TimeZoneId : suggestedZone);
                return;
            }
            SearchBox.Text = current.Name;
            ResultList.SelectedItem = ResultList.Items.OfType<Place>().FirstOrDefault(place =>
                place.Name == current.Name && place.CountryCode == current.CountryCode && place.Region == current.Region);
            SelectZone(!string.IsNullOrEmpty(current.TimeZoneId) ? current.TimeZoneId : (ResultList.SelectedItem as Place)?.TimeZoneId);
        }

        private void SelectZone(string id) =>
            ZoneBox.SelectedItem = string.IsNullOrEmpty(id) ? null : ZoneBox.Items.OfType<ZoneChoice>().FirstOrDefault(zone => zone.Id == id);

        private string ChosenZone => (ZoneBox.SelectedItem as ZoneChoice)?.Id;

        private void Zone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready) UpdateState();
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // The list and the combo box report their own selection changes up to the tabs.
            if (ready && ReferenceEquals(e.OriginalSource, Tabs)) UpdateState();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            IReadOnlyList<Place> found = places.Search(SearchBox.Text, MostCities);
            ResultList.ItemsSource = found;
            if (found.Count > 0) ResultList.SelectedIndex = 0;
            if (ready) UpdateState();
        }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready) return;
            if (ResultList.SelectedItem is Place place) SelectZone(place.TimeZoneId);
            UpdateState();
        }

        private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultList.SelectedItem != null && OkButton.IsEnabled) Accept();
        }

        private void CoordinatesInput_Changed(object sender, RoutedEventArgs e)
        {
            if (ready) UpdateState();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

        private CountryInfo ChosenCountry => CountryBox.SelectedItem as CountryInfo;

        private void UpdateState()
        {
            if (Tabs.SelectedIndex == CityTab)
            {
                OkButton.IsEnabled = ResultList.SelectedItem is Place && ChosenZone != null;
                return;
            }
            string text = CoordinatesBox.Text;
            coordinates = CoordinateParser.Parse(text, ChosenCountry?.Code, places);
            if (ChosenCountry == null) CoordinatesResult.Text = Text("hebrew_place_need_country");
            else if (string.IsNullOrWhiteSpace(text)) CoordinatesResult.Text = "";
            else CoordinatesResult.Text = Describe(coordinates);
            if (ChosenCountry != null && coordinates.Success)
            {
                // The zone follows the point while the point changes it; once the user picks another it stays.
                string suggestion = places.NearestTimeZone(ChosenCountry.Code, coordinates.Latitude, coordinates.Longitude);
                if (suggestion != suggestedZone)
                {
                    suggestedZone = suggestion;
                    SelectZone(suggestion);
                }
            }
            OkButton.IsEnabled = ChosenCountry != null && coordinates.Success && ChosenZone != null;
        }

        private void Accept()
        {
            if (ChosenZone == null) return;
            if (Tabs.SelectedIndex == CityTab && ResultList.SelectedItem is Place place)
                Result = HebrewPlace.FromPlace(place) with { TimeZoneId = ChosenZone };
            else if (Tabs.SelectedIndex == CoordinatesTab && coordinates is { Success: true } && ChosenCountry != null)
                Result = HebrewPlace.FromCoordinates(coordinates.Latitude, coordinates.Longitude, ChosenCountry, ChosenZone);
            else
                return;
            DialogResult = true;
        }

        private static string Describe(CoordinateParseResult result)
        {
            if (!result.Success)
                return Text(result.Problem switch
                {
                    CoordinateProblem.Empty => "coordinate_problem_empty",
                    CoordinateProblem.NeedTwoNumbers => "coordinate_problem_need_two",
                    CoordinateProblem.TooManyNumbers => "coordinate_problem_too_many",
                    CoordinateProblem.TwoLatitudes => "coordinate_problem_two_lat",
                    CoordinateProblem.TwoLongitudes => "coordinate_problem_two_lng",
                    CoordinateProblem.LatitudeOutOfRange => "coordinate_problem_lat_range",
                    CoordinateProblem.LongitudeOutOfRange => "coordinate_problem_lng_range",
                    _ => "coordinate_problem_neither",
                });

            var lines = new List<string>
            {
                string.Format(Text("coordinate_result"), CoordinateParser.Format(result.Latitude, result.Longitude)),
            };
            if (result.Warning == CoordinateWarning.OrderAssumed) lines.Add(Text("coordinate_warning_assumed"));
            else lines.Add(Text(result.Basis switch
            {
                CoordinateOrderBasis.Labeled => "coordinate_basis_labeled",
                CoordinateOrderBasis.OnlyOneFits => "coordinate_basis_only_one",
                CoordinateOrderBasis.ByCountry => "coordinate_basis_country",
                _ => "coordinate_basis_assumed",
            }));
            if (result.Warning == CoordinateWarning.FarFromCountry)
                lines.Add(string.Format(Text("coordinate_warning_far"), result.DistanceToCountryKm));
            return string.Join(Environment.NewLine, lines);
        }

        private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;
    }
}
