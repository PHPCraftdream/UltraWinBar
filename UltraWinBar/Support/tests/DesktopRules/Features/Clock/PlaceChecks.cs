using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UltraWinBar.Utilities;

// The place data (cities of 10 000 inhabitants and more), the choice of a place for the Hebrew date and how it is
// stored and shown in the settings.
internal static class PlaceChecks
{
    internal static void Run(string[] args, DirectoryInfo repositoryRoot)
    {
        var places = PlaceDatabase.Shared;

        // The data embedded in the program: a plausible size, a sorted list of countries each with places.
        if (places.Count < 40000 || places.Count > 60000) throw new Exception($"Unexpected number of places: {places.Count}.");
        if (places.Countries.Count < 200 || places.Countries.Count > 240) throw new Exception($"Unexpected number of countries: {places.Countries.Count}.");
        for (int i = 1; i < places.Countries.Count; i++)
            if (string.CompareOrdinal(places.Countries[i - 1].Name, places.Countries[i].Name) > 0)
                throw new Exception($"Countries must be sorted by name: {places.Countries[i - 1].Name} before {places.Countries[i].Name}.");
        foreach (var country in places.Countries)
            if (country.Code.Length != 2 || country.Name.Length == 0 || !places.PlacesOf(country.Code).Any())
                throw new Exception($"A country without a code, a name or a place: {country}.");
        if (places.FindCountry("il")?.Name != "Israel" || places.FindCountry("US")?.Name != "United States" || places.FindCountry("XX") != null || places.FindCountry(null) != null)
            throw new Exception("Countries are found by their code, in either case.");
        if (places.PlacesOf("IL").Count() < 30 || places.PlacesOf("").Any() || places.PlacesOf("XX").Any())
            throw new Exception("The places of a country, and none for no country.");
        Console.WriteLine($"PASS: {places.Count} places in {places.Countries.Count} countries load from the embedded data; countries are sorted and every one has places.");

        // Every place: a name, coordinates in range, a population of at least 10 000.
        for (int i = 0; i < places.Count; i++)
        {
            Place place = places[i];
            if (place.Name.Length == 0 || place.Latitude < -90 || place.Latitude > 90 || place.Longitude < -180 || place.Longitude > 180 ||
                place.Population < 10000 || place.CountryCode.Length != 2)
                throw new Exception($"A place that does not belong in the data: {place}.");
        }
        // A handful of cities with their well-known coordinates, read from the data.
        foreach (var (query, country, latitude, longitude) in new[]
        {
            ("Jerusalem", "IL", 31.7683, 35.2137), ("Tel Aviv", "IL", 32.0853, 34.7818), ("Haifa", "IL", 32.7940, 34.9896),
            ("Safed", "IL", 32.9646, 35.4960), ("Tiberias", "IL", 32.7959, 35.5300), ("Eilat", "IL", 29.5577, 34.9519),
            ("Bnei Brak", "IL", 32.0807, 34.8338), ("Moscow", "RU", 55.7558, 37.6173), ("London", "GB", 51.5074, -0.1278),
            ("Paris", "FR", 48.8566, 2.3522), ("New York", "US", 40.7128, -74.0060), ("Sydney", "AU", -33.8688, 151.2093),
            ("Tokyo", "JP", 35.6762, 139.6503), ("Cape Town", "ZA", -33.9249, 18.4241), ("Buenos Aires", "AR", -34.6037, -58.3816),
            ("Reykjavik", "IS", 64.1466, -21.9426), ("Ushuaia", "AR", -54.8019, -68.3030), ("Nuku", "TO", -21.1394, -175.2049),
        })
        {
            var found = places.Search(query, 20).FirstOrDefault(place => place.CountryCode == country &&
                PlaceDatabase.DistanceKm(place.Latitude, place.Longitude, latitude, longitude) < 20);
            if (found == null) throw new Exception($"{query} ({country}) is not in the data, or lies more than 20 km from its true place.");
        }
        if (Math.Abs(PlaceDatabase.DistanceKm(40.7128, -74.0060, 51.5074, -0.1278) - 5570) > 15 ||
            Math.Abs(PlaceDatabase.DistanceKm(31.7683, 35.2137, 32.0853, 34.7818) - 54) > 1.5 ||
            Math.Abs(PlaceDatabase.DistanceKm(0, 0, 90, 0) - Math.PI / 2 * 6371) > 1e-6 ||
            Math.Abs(PlaceDatabase.DistanceKm(10, 20, -10, -160) - Math.PI * 6371) > 0.01)
            throw new Exception("Great-circle distances on the sphere are off.");
        Console.WriteLine("PASS: every place has a name, a country, coordinates in range and 10 000 inhabitants; well-known cities lie where they should.");

        // The search: all words, in any order, prefixes, accents and case ignored, exact names first, bigger first.
        string First(string query) => places.Search(query, 5).FirstOrDefault()?.Display;
        if (First("jerusalem") != "Jerusalem, Jerusalem, Israel" || First("JERUSAL") != "Jerusalem, Jerusalem, Israel" ||
            First("  jeru  ") != "Jerusalem, Jerusalem, Israel")
            throw new Exception($"Jerusalem is not found by its name or a start of it: {First("jerusalem")}.");
        if (!places.Search("new york", 5).Any(place => place.Name == "New York City") || First("moscow") != "Moscow, Moscow, Russia")
            throw new Exception("Big cities come first: New York, Moscow.");
        string paris = First("paris texas");
        if (paris == null || !paris.StartsWith("Paris, Texas")) throw new Exception($"Words of the region find a place: {paris}.");
        if (First("texas paris") != paris || First("paris tex") != paris) throw new Exception("The words may come in any order and the last may be a start.");
        if (!places.Search("paris", 50).Any(place => place.Country == "France") || places.Search("paris", 50)[0].Country != "France")
            throw new Exception("The biggest Paris is first.");
        if (First("Reykjavík") == null || First("Reykjavík") != First("reykjavik") || First("Zürich") == null || First("zurich") != First("ZÜRICH") ||
            First("São Paulo") == null)
            throw new Exception("Accents do not matter.");
        if (places.Search("", 5).Count != 0 || places.Search("   ", 5).Count != 0 || places.Search(null, 5).Count != 0 ||
            places.Search("jerusalem", 0).Count != 0 || places.Search("xqzv", 5).Count != 0 || places.Search("Иерусалим", 5).Count != 0 ||
            places.Search("\uD800", 5).Count != 0 || places.Search("!!!", 5).Count != 0)
            throw new Exception("Nothing is found for an empty, unknown, Russian or broken query.");
        if (places.Search("san", 7).Count != 7 || places.Search("san", 500).Count != 500)
            throw new Exception("The limit of results is kept.");
        var big = places.Search("san", 100);
        for (int i = 1; i < big.Count; i++)
            if (big[i - 1].Population < big[i].Population && Rank(big[i - 1].Name, "san") == Rank(big[i].Name, "san"))
                throw new Exception($"Within a rank, bigger places come first: {big[i - 1]} before {big[i]}.");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 50; i++) places.Search("san fran", 60);
        if (timer.ElapsedMilliseconds > 3000) throw new Exception($"Search is too slow for typing: {timer.ElapsedMilliseconds / 50} ms.");
        Console.WriteLine("PASS: the search takes every word as the start of a word of the name, region or country, in any order; exact names, then names that start so, then the rest, bigger first; accents ignored; nothing for empty or unknown.");

        CheckBrokenData();
        CheckLoadingFromTheResource();
        CheckHebrewPlace(places);
        CheckTimeZones(places);
        CheckSecondTimeZone(repositoryRoot);
        CheckClockZones(repositoryRoot);
        CheckWindowsAndStrings(repositoryRoot);
    }

    // 0: named exactly so, 1: the name starts so, 2: a later word of the name starts so, 3: only the region or the country does.
    private static int Rank(string name, string query)
    {
        string folded = PlaceDatabase.Fold(name);
        return folded == query ? 0
            : folded.StartsWith(query, StringComparison.Ordinal) ? 1
            : folded.Split(' ').Any(word => word.StartsWith(query, StringComparison.Ordinal)) ? 2 : 3;
    }

    private static PlaceDatabase Parse(string text) => PlaceDatabase.Parse(new StringReader(text));

    private static void CheckBrokenData()
    {
        const string header = "places v2\t2\t1\t1\t1\n";
        const string rest = "C\tIL\tIsrael\nR\t0\tHaifa District\nZ\tAsia/Jerusalem\n";
        var small = Parse(header + rest + "P\tHaifa\t0\t0\t32.794\t34.9896\t285316\t0\nP\tNesher\t0\t0\t32.77\t35.04\t23000\t\n");
        if (small.Count != 2 || small.Search("nes", 5)[0].Display != "Nesher, Haifa District, Israel" ||
            small.Search("district", 5).Count != 2 || small.Search("israel haifa", 5)[0].Name != "Haifa")
            throw new Exception("A small file of places must read and search.");
        if (small.TimeZones.Count != 1 || small.TimeZones[0] != "Asia/Jerusalem" || small.Search("haifa", 1)[0].TimeZoneId != "Asia/Jerusalem" ||
            small.Search("nesher", 1)[0].TimeZoneId != "")
            throw new Exception("A place has its time zone, or none.");
        var withoutRegion = Parse("places v2\t1\t1\t0\t0\nC\tIL\tIsrael\nP\tNowhere\t\t0\t1\t2\t10000\t\n");
        if (withoutRegion[0].Region != "" || withoutRegion[0].Display != "Nowhere, Israel") throw new Exception("A place may have no region.");
        var withoutCountry = Parse("places v2\t1\t0\t0\t0\nP\tNowhere\t\t\t1\t2\t10000\t\n");
        if (withoutCountry[0].Country != "" || withoutCountry[0].CountryCode != "" || withoutCountry.Search("nowhere", 5).Count != 1)
            throw new Exception("A place may have no country.");
        if (withoutCountry.NearestTimeZone("", 1, 2) != "" || small.NearestTimeZone("IL", 32.7, 35) != "Asia/Jerusalem" || small.NearestTimeZone("XX", 32.7, 35) != "Asia/Jerusalem")
            throw new Exception("The nearest time zone: none when no place has one.");

        foreach (var (what, text) in new[]
        {
            ("empty", ""),
            ("the old version", "places v1\t0\t0\t0\n"),
            ("wrong version", "places v3\t0\t0\t0\t0\n"),
            ("short header", "places v2\t0\t0\t0\n"),
            ("not a number in the header", "places v2\tmany\t0\t0\t0\n"),
            ("fewer places than the header says", header + rest + "P\tHaifa\t0\t0\t32.794\t34.9896\t285316\t0\n"),
            ("more places than the header says", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\t1\t2\t3\t0\nP\tB\t0\t0\t1\t2\t3\t0\n"),
            ("fewer zones than the header says", "places v2\t0\t0\t0\t1\n"),
            ("a country number out of range", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t5\t1\t2\t3\t0\n"),
            ("a region number out of range", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t7\t0\t1\t2\t3\t0\n"),
            ("a zone number out of range", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\t1\t2\t3\t4\n"),
            ("a zone with no name", "places v2\t0\t0\t0\t1\nZ\t\n"),
            ("a region of no country", "places v2\t0\t0\t1\t0\nR\t0\tOdd\n"),
            ("latitude out of range", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\t91\t2\t3\t0\n"),
            ("longitude out of range", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\t1\t181\t3\t0\n"),
            ("a coordinate that is not a number", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\tnorth\t2\t3\t0\n"),
            ("a negative population", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\t1\t2\t-3\t0\n"),
            ("too few fields", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\t1\n"),
            ("a place without the zone field", "places v2\t1\t1\t1\t1\n" + rest + "P\tA\t0\t0\t1\t2\t3\n"),
            ("an unknown kind of line", "places v2\t0\t0\t0\t0\nX\tfoo\n"),
        })
            try
            {
                Parse(text);
                throw new Exception($"Broken place data was accepted: {what}.");
            }
            catch (InvalidDataException) { }
        Console.WriteLine("PASS: broken place data (wrong header, counts, numbers, coordinates, zones, fields) is refused with a clear error; regions, countries and zones may be missing.");
    }

    // The time zones: each place has a known one, the user's choice is kept and read back, and the zones read as the law of the zone says.
    private static void CheckTimeZones(PlaceDatabase places)
    {
        if (places.TimeZones.Count < 300 || places.TimeZones.Count > 400) throw new Exception($"Unexpected number of time zones: {places.TimeZones.Count}.");
        for (int i = 1; i < places.TimeZones.Count; i++)
            if (string.CompareOrdinal(places.TimeZones[i - 1], places.TimeZones[i]) >= 0) throw new Exception("Time zones are sorted by name, each once.");
        var known = new HashSet<string>(places.TimeZones);
        int without = 0;
        for (int i = 0; i < places.Count; i++)
        {
            string zone = places[i].TimeZoneId;
            if (zone.Length == 0) without++;
            else if (!known.Contains(zone)) throw new Exception($"A time zone that is not in the list: {places[i]}.");
        }
        if (without > places.Count / 100) throw new Exception($"{without} places have no time zone.");
        foreach (var (query, country, zone) in new[]
        {
            ("Jerusalem", "IL", "Asia/Jerusalem"), ("Tel Aviv", "IL", "Asia/Jerusalem"), ("Moscow", "RU", "Europe/Moscow"), ("London", "GB", "Europe/London"),
            ("New York", "US", "America/New_York"), ("Sydney", "AU", "Australia/Sydney"), ("Tokyo", "JP", "Asia/Tokyo"), ("Buenos Aires", "AR", "America/Argentina/Buenos_Aires"),
            ("Ushuaia", "AR", "America/Argentina/Ushuaia"), ("Kolkata", "IN", "Asia/Kolkata"), ("Kathmandu", "NP", "Asia/Kathmandu"),
        })
        {
            var place = places.Search(query, 20).FirstOrDefault(item => item.CountryCode == country);
            if (place == null || place.TimeZoneId != zone) throw new Exception($"{query} must be in {zone}, is in {place?.TimeZoneId}.");
        }
        if (places.NearestTimeZone("IL", 32.0853, 34.7818) != "Asia/Jerusalem" || places.NearestTimeZone("US", 40.7, -74.0) != "America/New_York" ||
            places.NearestTimeZone("AR", -54.8, -68.3) != "America/Argentina/Ushuaia" || places.NearestTimeZone(null, 55.75, 37.6) != "Europe/Moscow" ||
            places.NearestTimeZone("XX", 55.75, 37.6) != "Europe/Moscow")
            throw new Exception("The nearest time zone of a point.");

        // The user's choice is stored with the place and survives the settings file.
        var jerusalem = places.Search("jerusalem", 1)[0];
        var chosen = HebrewPlace.FromPlace(jerusalem) with { TimeZoneId = "Asia/Hebron" };
        var custom = HebrewPlace.FromCoordinates(31.7, 35.2, places.FindCountry("IL"), "Asia/Jerusalem");
        if (HebrewPlace.FromPlace(jerusalem).TimeZoneId != "Asia/Jerusalem" || chosen.TimeZoneId != "Asia/Hebron" || custom.TimeZoneId != "Asia/Jerusalem" ||
            HebrewPlace.FromCoordinates(31.7, 35.2, null).TimeZoneId != null || chosen == HebrewPlace.FromPlace(jerusalem))
            throw new Exception("A place has the time zone chosen for it.");
        var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        foreach (var place in new[] { chosen, custom })
            if (JsonSerializer.Deserialize<HebrewPlace>(JsonSerializer.Serialize(place, options), options) != place) throw new Exception("The time zone is written to the settings and read back.");
        // A place saved by an older version has no zone.
        var older = JsonSerializer.Deserialize<HebrewPlace>("{\"Name\":\"Haifa\",\"Latitude\":32.794,\"Longitude\":34.9896}", options);
        if (older.TimeZoneId != null) throw new Exception("A place saved without a zone has none.");

        // The zones as the computer's rules read them, in winter and in summer; "UTC+02:00" is the offset on that date.
        var winter = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var summer = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        foreach (var (zone, winterOffset, summerOffset) in new[]
        {
            ("Asia/Jerusalem", 2.0, 3.0), ("America/New_York", -5.0, -4.0), ("Europe/London", 0.0, 1.0), ("Europe/Moscow", 3.0, 3.0), ("Asia/Kolkata", 5.5, 5.5),
            ("Asia/Kathmandu", 5.75, 5.75), ("Australia/Sydney", 11.0, 10.0), ("UTC", 0.0, 0.0),
        })
        {
            if (ZoneClock.Offset(zone, winter) != TimeSpan.FromHours(winterOffset) || ZoneClock.Offset(zone, summer) != TimeSpan.FromHours(summerOffset))
                throw new Exception($"{zone}: the offsets in winter and summer are {ZoneClock.Offset(zone, winter)} and {ZoneClock.Offset(zone, summer)}.");
        }
        if (ZoneClock.Now("Asia/Jerusalem", winter) != new DateTime(2026, 1, 15, 14, 0, 0) || ZoneClock.Now("Asia/Jerusalem", summer) != new DateTime(2026, 7, 15, 15, 0, 0) ||
            ZoneClock.Now("Asia/Jerusalem", DateTime.SpecifyKind(winter, DateTimeKind.Unspecified)) != new DateTime(2026, 1, 15, 14, 0, 0))
            throw new Exception("The time in a zone is the time in UTC and the offset of that moment.");
        if (ZoneClock.Describe("Asia/Jerusalem", winter) != "Asia/Jerusalem (UTC+02:00)" || ZoneClock.Describe("America/New_York", summer) != "America/New_York (UTC-04:00)" ||
            ZoneClock.Describe("Asia/Kathmandu", winter) != "Asia/Kathmandu (UTC+05:45)" || ZoneClock.Describe("Nowhere/Land", winter) != "Nowhere/Land" ||
            ZoneClock.FormatOffset(TimeSpan.FromMinutes(-210)) != "-03:30" || ZoneClock.FormatOffset(TimeSpan.Zero) != "+00:00")
            throw new Exception("Zones are described by their id and offset.");
        if (ZoneClock.Find("") != null || ZoneClock.Find(null) != null || ZoneClock.Find("Nowhere/Land") != null || ZoneClock.Now("Nowhere/Land", winter) != null ||
            ZoneClock.Offset("Nowhere/Land", winter) != null || ZoneClock.ToUtc("Nowhere/Land", winter) != null)
            throw new Exception("An unknown zone gives nothing.");
        // The clocks of New York go forward at 02:00 on Sunday 8 March 2026: 02:30 does not exist, 01:30 and 03:30 do.
        if (ZoneClock.ToUtc("America/New_York", new DateTime(2026, 3, 8, 1, 30, 0)) != new DateTime(2026, 3, 8, 6, 30, 0) ||
            ZoneClock.ToUtc("America/New_York", new DateTime(2026, 3, 8, 3, 30, 0)) != new DateTime(2026, 3, 8, 7, 30, 0) ||
            ZoneClock.ToUtc("America/New_York", new DateTime(2026, 3, 8, 2, 30, 0)) != new DateTime(2026, 3, 8, 7, 30, 0))
            throw new Exception("Times at the change to summer time.");
        // And back at 02:00 on Sunday 1 November 2026: 01:30 comes twice, the first time as summer time.
        if (ZoneClock.ToUtc("America/New_York", new DateTime(2026, 11, 1, 1, 30, 0)) != new DateTime(2026, 11, 1, 5, 30, 0) ||
            ZoneClock.ToUtc("America/New_York", new DateTime(2026, 11, 1, 2, 30, 0)) != new DateTime(2026, 11, 1, 7, 30, 0))
            throw new Exception("Times at the change to winter time.");
        // Every zone of the data that this computer knows reads an offset between -12 and +14 hours; most are known.
        int readable = 0;
        foreach (string id in places.TimeZones)
        {
            TimeSpan? offset = ZoneClock.Offset(id, winter);
            if (offset == null) continue;
            readable++;
            if (offset < TimeSpan.FromHours(-12) || offset > TimeSpan.FromHours(14)) throw new Exception($"{id}: the offset {offset} is out of range.");
        }
        if (readable < places.TimeZones.Count * 9 / 10) throw new Exception($"Only {readable} of {places.TimeZones.Count} time zones are known to this computer.");
        ZoneClock.Refresh();
        if (ZoneClock.Find("Asia/Jerusalem") == null) throw new Exception("A zone is found again after the cache is cleared.");
        Console.WriteLine($"PASS: {places.TimeZones.Count} time zones, each place in one; the user's zone is kept with the place and in the settings; {readable} zones read their offsets from the computer, in winter and in summer, with the changes of the clocks.");
    }

    private static void CheckLoadingFromTheResource()
    {
        var assembly = typeof(PlaceDatabase).Assembly;
        using (var resource = assembly.GetManifestResourceStream(PlaceDatabase.ResourceName))
        {
            if (resource == null || resource.Length < 500_000 || resource.Length > 3_000_000)
                throw new Exception("The place data must be embedded, as a gzip file of a plausible size.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var loaded = PlaceDatabase.Load(resource);
            if (loaded.Count != PlaceDatabase.Shared.Count || timer.ElapsedMilliseconds > 5000)
                throw new Exception($"Loading the embedded data took {timer.ElapsedMilliseconds} ms or gave {loaded.Count} places.");
        }
        if (!ReferenceEquals(PlaceDatabase.Shared, PlaceDatabase.Shared)) throw new Exception("The shared place data is loaded once.");
        // A text that is not gzip must not look like data.
        try { PlaceDatabase.Load(new MemoryStream(Encoding.UTF8.GetBytes("places v2\t0\t0\t0\t0\n"))); throw new Exception("Plain text was read as gzip."); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: the place data is embedded as gzip, loads once and quickly, and plain text is not taken for it.");
    }

    private static void CheckHebrewPlace(PlaceDatabase places)
    {
        var jerusalem = places.Search("jerusalem", 1)[0];
        var city = HebrewPlace.FromPlace(jerusalem);
        var coordinates = HebrewPlace.FromCoordinates(32.0853, 34.7818, places.FindCountry("IL"));
        if (city.IsCustom || city.Display != "Jerusalem, Jerusalem, Israel" || city.CountryCode != "IL" ||
            Math.Abs(city.Latitude - jerusalem.Latitude) > 1e-9)
            throw new Exception("A chosen city keeps its name, region, country and coordinates.");
        if (!coordinates.IsCustom || coordinates.Display != "32.0853° N, 34.7818° E, Israel" || coordinates.Name != null || coordinates.CountryCode != "IL")
            throw new Exception($"Typed-in coordinates are shown with their country: {coordinates.Display}.");
        if (HebrewPlace.FromCoordinates(-33.8688, 151.2093, null).Display != "33.8688° S, 151.2093° E")
            throw new Exception("Coordinates without a country are shown alone.");
        if (city != HebrewPlace.FromPlace(jerusalem) || city == coordinates) throw new Exception("Places are equal when all they say is equal.");

        // The setting: none until chosen, written to the settings file and read back as it was; nothing written for none.
        var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
        var property = settingsType.GetProperty("HebrewPlace") ?? throw new Exception("Settings.HebrewPlace is missing.");
        var options = new JsonSerializerOptions
        {
            IgnoreReadOnlyProperties = true,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        object settings = Activator.CreateInstance(settingsType);
        if (property.GetValue(settings) != null) throw new Exception("No place is chosen by default.");
        if (JsonSerializer.Serialize(settings, settingsType, options).Contains("HebrewPlace")) throw new Exception("An unset place must not be written to the settings file.");
        foreach (var chosen in new[] { city, coordinates, HebrewPlace.FromCoordinates(-54.8019, -68.303, places.FindCountry("AR")) })
        {
            property.SetValue(settings, chosen);
            string json = JsonSerializer.Serialize(settings, settingsType, options);
            if (json.Contains("Display")) throw new Exception("The display text is derived and must not be stored.");
            object back = JsonSerializer.Deserialize(json, settingsType, options);
            if (!Equals(property.GetValue(back), chosen)) throw new Exception($"The chosen place did not survive the settings file: {chosen.Display}.");
        }
        // Setting the same place again is no change; clearing it is one.
        int changes = 0;
        var changed = (System.ComponentModel.INotifyPropertyChanged)settings;
        changed.PropertyChanged += (_, e) => { if (e.PropertyName == "HebrewPlace") changes++; };
        property.SetValue(settings, HebrewPlace.FromPlace(jerusalem));
        property.SetValue(settings, HebrewPlace.FromPlace(jerusalem));
        property.SetValue(settings, null);
        property.SetValue(settings, null);
        if (changes != 2) throw new Exception($"The settings must announce a change of place only when it changes: {changes} announcements.");
        Console.WriteLine("PASS: the chosen place (a city or typed-in coordinates) is shown, equal by value, written to the settings file and read back as it was; unset by default and not written when unset.");
    }

    // The second time under the clock's own: the setting, the names it shows, and the clock that carries it.
    private static void CheckSecondTimeZone(DirectoryInfo root)
    {
        if (ZoneClock.ShortName("America/Argentina/Buenos_Aires") != "Buenos Aires" || ZoneClock.ShortName("Asia/Jerusalem") != "Jerusalem" ||
            ZoneClock.ShortName("UTC") != "UTC" || ZoneClock.ShortName("") != "" || ZoneClock.ShortName(null) != "")
            throw new Exception("A zone is shown by the name of its place.");

        var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
        var property = settingsType.GetProperty("SecondTimeZone") ?? throw new Exception("Settings.SecondTimeZone is missing.");
        var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        object settings = Activator.CreateInstance(settingsType);
        if ((string)property.GetValue(settings) != "") throw new Exception("No second time zone is chosen by default.");
        int changes = 0;
        ((System.ComponentModel.INotifyPropertyChanged)settings).PropertyChanged += (_, e) => { if (e.PropertyName == "SecondTimeZone") changes++; };
        property.SetValue(settings, "Asia/Jerusalem");
        property.SetValue(settings, "Asia/Jerusalem");
        property.SetValue(settings, null);
        property.SetValue(settings, "");
        if (changes != 2 || (string)property.GetValue(settings) != "") throw new Exception($"The second time zone announces a change only when it changes, and none is empty: {changes} announcements.");
        property.SetValue(settings, "America/New_York");
        object back = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, settingsType, options), settingsType, options);
        if ((string)property.GetValue(back) != "America/New_York") throw new Exception("The second time zone survives the settings file.");

        var clockType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Controls.Clock");
        if (clockType.GetProperty("SecondTimeText")?.PropertyType != typeof(string) || clockType.GetProperty("SecondTimeVisibility")?.PropertyType != typeof(System.Windows.Visibility))
            throw new Exception("The clock carries the second time and whether it shows.");
        string clock = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml"));
        string clockCode = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml.cs"));
        int hebrewTemplate = clock.IndexOf("x:Key=\"HebrewClockTemplate\"", StringComparison.Ordinal);
        if (!clock.Contains("x:Key=\"TwoZoneClockTemplate\"") || hebrewTemplate < 0 || clock.IndexOf("{Binding SecondTimeText}", hebrewTemplate, StringComparison.Ordinal) < 0 ||
            clock.IndexOf("{Binding SecondTimeVisibility}", hebrewTemplate, StringComparison.Ordinal) < 0)
            throw new Exception("The clock has a template of two times and the Hebrew template carries the second time.");
        if (!clockCode.Contains("\"TwoZoneClockTemplate\"") || !clockCode.Contains("nameof(Settings.SecondTimeZone)") || !clockCode.Contains("ZoneClock.Refresh()"))
            throw new Exception("The clock picks the two-time template, follows the setting, and reads the time zone rules again when the time changes.");
        Console.WriteLine("PASS: the second time zone is a setting (none by default, announced when it changes, kept in the settings file), shown by the name of its place under the clock's own time.");
    }

    // The three time zones of the clock: the settings, the names, which one the calendar goes by, and the window that sets them.
    private static void CheckClockZones(DirectoryInfo root)
    {
        var fresh = new Settings();
        if (fresh.MainTimeZone != "" || fresh.SecondPlace != null || fresh.ThirdPlace != null)
            throw new Exception("No place is chosen for the second and the third zone by default.");
        if (fresh.ThirdTimeZone != "" || fresh.ShowMainZoneName || !fresh.ShowSecondZoneName || !fresh.ShowThirdZoneName || fresh.CalendarZone != 0)
            throw new Exception("By default: no third zone, the main zone not named, the others named, the calendar by the main zone.");
        fresh.CalendarZone = 7;
        if (fresh.CalendarZone != 0) throw new Exception("A calendar zone that is none of the three is the main one.");
        fresh.CalendarZone = 2;
        if (fresh.CalendarZone != 2) throw new Exception("The calendar zone is kept.");
        fresh.ThirdTimeZone = null;
        if (fresh.ThirdTimeZone != "") throw new Exception("No third zone is an empty one.");
        var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true };
        var tokyo = HebrewPlace.FromCoordinates(35.6762, 139.6503, null, "Asia/Tokyo");
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { ThirdTimeZone = "Asia/Tokyo", ThirdPlace = tokyo, ShowMainZoneName = true, ShowSecondZoneName = false, CalendarZone = 1 }, options), options);
        if (back.ThirdTimeZone != "Asia/Tokyo" || back.ThirdPlace != tokyo || back.SecondPlace != null || !back.ShowMainZoneName || back.ShowSecondZoneName || back.CalendarZone != 1)
            throw new Exception("The zones, their places and their names are kept in the settings file.");

        var settings = Settings.Instance;
        string second = settings.SecondTimeZone, third = settings.ThirdTimeZone;
        int calendar = settings.CalendarZone;
        HebrewPlace place = settings.HebrewPlace, secondPlace = settings.SecondPlace, thirdPlace = settings.ThirdPlace;
        try
        {
            settings.SecondTimeZone = "Europe/Moscow";
            settings.ThirdTimeZone = "Nowhere/Land";
            settings.HebrewPlace = HebrewPlace.FromCoordinates(52.374, 4.8897, null, "Europe/Amsterdam");
            settings.SecondPlace = HebrewPlace.FromCoordinates(55.7558, 37.6173, null, "Europe/Moscow");
            settings.ThirdPlace = null;
            if (ClockZones.IdOf(ClockZones.Main) != "" || ClockZones.IdOf(ClockZones.Second) != "Europe/Moscow" || !ClockZones.IsSet(ClockZones.Main) ||
                !ClockZones.IsSet(ClockZones.Second) || ClockZones.IsSet(ClockZones.Third))
                throw new Exception("The main zone always is, a chosen one is when this computer knows it, an unknown one is not.");
            if (ClockZones.NameOf(ClockZones.Second) != "Moscow" || string.IsNullOrWhiteSpace(ClockZones.NameOf(ClockZones.Main)))
                throw new Exception($"The names of the zones: {ClockZones.NameOf(ClockZones.Main)} / {ClockZones.NameOf(ClockZones.Second)}.");
            settings.CalendarZone = ClockZones.Second;
            if (ClockZones.CalendarZone() != ClockZones.Second || ClockZones.CalendarPlace().TimeZoneId != "Europe/Moscow" || ClockZones.CalendarPlace().Latitude != 55.7558)
                throw new Exception("The calendar goes by the zone chosen, at the place of that zone, not at the main place with a shifted clock.");
            settings.CalendarZone = ClockZones.Main;
            if (ClockZones.CalendarPlace().TimeZoneId != "" || ClockZones.CalendarPlace().Latitude != 52.374)
                throw new Exception("By the main zone the calendar is at the main place, on the computer's clock.");
            settings.ThirdTimeZone = "Asia/Tokyo";
            settings.CalendarZone = ClockZones.Third;
            if (ClockZones.CalendarZone() != ClockZones.Third || ClockZones.CalendarPlace() != null)
                throw new Exception("A zone with no place has no place for the calendar: it shows no times rather than those of another place.");
            settings.ThirdTimeZone = "Nowhere/Land";
            settings.CalendarZone = ClockZones.Third;
            if (ClockZones.CalendarZone() != ClockZones.Main || ClockZones.CalendarPlace().TimeZoneId != "")
                throw new Exception("A zone that is not set is not followed: the calendar goes by the computer's clock.");
            settings.HebrewPlace = null;
            if (ClockZones.CalendarPlace() != null) throw new Exception("With no place there is no place for the calendar.");
        }
        finally
        {
            settings.SecondTimeZone = second;
            settings.ThirdTimeZone = third;
            settings.CalendarZone = calendar;
            settings.HebrewPlace = place;
            settings.SecondPlace = secondPlace;
            settings.ThirdPlace = thirdPlace;
        }
        if (string.IsNullOrWhiteSpace(ZoneClock.LocalName())) throw new Exception("The computer's zone has no name.");

        var clockType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Controls.Clock");
        if (clockType.GetProperty("ThirdTimeText")?.PropertyType != typeof(string) || clockType.GetProperty("ThirdTimeVisibility")?.PropertyType != typeof(System.Windows.Visibility))
            throw new Exception("The clock carries the third time and whether it shows.");
        string clock = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml"));
        string clockCode = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml.cs"));
        if (Regex.Matches(clock, @"\{Binding ThirdTimeText\}").Count != 2 || Regex.Matches(clock, @"\{Binding ThirdTimeVisibility\}").Count != 2)
            throw new Exception("Both templates of several times show the third one.");
        foreach (string needed in new[] { "nameof(Settings.ThirdTimeZone)", "nameof(Settings.ShowMainZoneName)", "nameof(Settings.ShowSecondZoneName)", "nameof(Settings.ShowThirdZoneName)", "nameof(Settings.CalendarZone)", "ClockZones.CalendarPlace()" })
            if (!clockCode.Contains(needed)) throw new Exception("The clock does not follow " + needed + ".");
        if (clock.Contains("ClockTip") || clock.Contains("<UserControl.ToolTip>") || clockCode.Contains("ClockTip"))
            throw new Exception("The clock shows no tool tip with the date.");

        string panel = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Zones", "ClockZonesPanel.xaml"));
        string panelCode = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Zones", "ClockZonesPanel.xaml.cs"));
        string properties = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "Shell", "Dialogs", "PropertiesWindow.xaml"));
        foreach (string checkBox in new[] { "ShowMainZoneName", "ShowSecondZoneName", "ShowThirdZoneName" })
            if (!panel.Contains("{Binding " + checkBox + ",")) throw new Exception("The zones panel has no check box for " + checkBox + ".");
        foreach (Match handler in Regex.Matches(panel, @"(?:SelectionChanged|Click)=""([A-Za-z0-9_]+)"""))
            if (!Regex.IsMatch(panelCode, @"void\s+" + handler.Groups[1].Value + @"\s*\(")) throw new Exception("The zones panel names a handler it has not: " + handler.Groups[1].Value);
        if (!properties.Contains("<controls:ClockZonesPanel />"))
            throw new Exception("The time zones are in the settings.");
        string calendarSettings = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Calendar", "Window", "CalendarSettingsWindow.xaml.cs"));
        if (calendarSettings.Contains("SecondTimeZone") || !calendarSettings.Contains("Settings.Instance.CalendarZone"))
            throw new Exception("The calendar's settings choose among the clock's zones and do not set the second one.");
        string placeWindow = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "Shell", "Dialogs", "PlaceWindow.xaml"));
        if (placeWindow.Contains("ZoneBox")) throw new Exception("The place window has no time zone to choose: the zone is one of the clock's three.");

        string languages = Path.Combine(root.FullName, "UltraWinBar", "Assets", "Languages");
        foreach (string file in new[] { Path.Combine("Group01", "English.xaml"), Path.Combine("Group06", "русский.xaml"), Path.Combine("Group05", "беларуская.xaml") })
        {
            string text = File.ReadAllText(Path.Combine(languages, file));
            foreach (string key in new[] { "zones_heading", "zone_main", "zone_second", "zone_third", "zone_show_name", "zone_none", "calendar_settings_zone", "calendar_zone_main", "zone_place", "zone_computer", "zones_tab", "hebrew_date_tab", "hebrew_place_in_zones" })
                if (!text.Contains("x:Key=\"" + key + "\"")) throw new Exception($"{file} lacks {key}.");
        }
        Console.WriteLine("PASS: three time zones (the computer's, a second and a third, each named or not), the calendar goes by the one chosen and falls back to the computer's, the settings carry them, and the clock shows no date tip.");
    }

    private static void CheckWindowsAndStrings(DirectoryInfo root)
    {
        string dialogs = Path.Combine(root.FullName, "UltraWinBar", "Shell", "Dialogs");
        string properties = File.ReadAllText(Path.Combine(dialogs, "PropertiesWindow.xaml"));
        string propertiesCode = File.ReadAllText(Path.Combine(dialogs, "PropertiesWindow.xaml.cs"));
        string window = File.ReadAllText(Path.Combine(dialogs, "PlaceWindow.xaml"));
        string windowCode = File.ReadAllText(Path.Combine(dialogs, "PlaceWindow.xaml.cs"));

        // Every handler a window names exists in its code.
        foreach (var (xaml, code, name) in new[] { (properties, propertiesCode, "PropertiesWindow"), (window, windowCode, "PlaceWindow") })
            foreach (Match handler in Regex.Matches(xaml, @"\b(?:Click|SelectionChanged|TextChanged|MouseDoubleClick)=""([A-Za-z0-9_]+)"""))
                if (!Regex.IsMatch(code, @"void\s+" + handler.Groups[1].Value + @"\s*\("))
                    throw new Exception($"{name}.xaml names the handler {handler.Groups[1].Value}, which {name}.xaml.cs does not have.");

        // The settings window: the notification area has its sub-tabs; the place of the Hebrew date is the place of the main
        // time zone, chosen (and cleared, and followed) in the zones panel of the Time zones tab.
        string zonesPanelCode = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Zones", "ClockZonesPanel.xaml.cs"));
        foreach (string tab in new[] { "general_heading", "clock_heading", "zones_tab", "hebrew_date_tab" })
            if (!properties.Contains("<TabItem Header=\"{DynamicResource " + tab + "}\">")) throw new Exception("The notification area has no sub-tab " + tab + ".");
        if (!properties.Contains("x:Name=\"cbShowHebrewDate\"") || !properties.Contains("<controls:ClockZonesPanel />") || properties.Contains("HebrewPlaceText"))
            throw new Exception("The Hebrew date has its tab, and its place is chosen only in the zones panel.");
        if (!zonesPanelCode.Contains("Settings.Instance.HebrewPlace = place") || !zonesPanelCode.Contains("nameof(Settings.HebrewPlace)") ||
            !zonesPanelCode.Contains("MainPlaceChoose_Click") || !zonesPanelCode.Contains("MainPlaceClear_Click"))
            throw new Exception("The zones panel must store the main place, clear it and follow changes of it.");

        // The strings: those the windows use are in English, and Russian and Belarusian have all of the new ones.
        var used = new HashSet<string>();
        foreach (string text in new[] { properties, window })
            foreach (Match key in Regex.Matches(text, @"\{DynamicResource ((?:hebrew_place|coordinate_|cancel_dialog|ok_dialog)[a-z_]*)\}")) used.Add(key.Groups[1].Value);
        foreach (string text in new[] { propertiesCode, windowCode })
            foreach (Match key in Regex.Matches(text, @"(?:Text|FindResource)\(""((?:hebrew_place|coordinate_)[a-z_]*)""\)")) used.Add(key.Groups[1].Value);
        foreach (string key in new[] { "coordinate_result", "coordinate_warning_far", "coordinate_warning_assumed" }) used.Add(key);
        foreach (CoordinateProblem problem in Enum.GetValues(typeof(CoordinateProblem)).Cast<CoordinateProblem>().Where(p => p != CoordinateProblem.None))
            if (!windowCode.Contains("CoordinateProblem." + problem) && problem != CoordinateProblem.NeitherOrderFits)
                throw new Exception($"The window does not explain the problem {problem}.");
        string languages = Path.Combine(root.FullName, "UltraWinBar", "Assets", "Languages");
        HashSet<string> Keys(string path) => new HashSet<string>(Regex.Matches(File.ReadAllText(path), "x:Key=\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));
        var english = Keys(Path.Combine(languages, "Group01", "English.xaml"));
        var wanted = english.Where(key => key.StartsWith("hebrew_place") || key.StartsWith("coordinate_") || key == "cancel_dialog").ToArray();
        if (wanted.Length < 25 || used.Any(key => !english.Contains(key)))
            throw new Exception($"English lacks strings the windows use: {string.Join(", ", used.Where(key => !english.Contains(key)))}.");
        foreach (string language in new[] { Path.Combine("Group06", "русский.xaml"), Path.Combine("Group05", "беларуская.xaml") })
        {
            var keys = Keys(Path.Combine(languages, language));
            var missing = wanted.Where(key => !keys.Contains(key)).ToArray();
            if (missing.Length > 0) throw new Exception($"{language} lacks: {string.Join(", ", missing)}.");
        }
        Console.WriteLine("PASS: the settings window and the place window name only handlers they have, the place follows the Hebrew date checkbox, and every string they use is in English, Russian and Belarusian.");
    }
}
