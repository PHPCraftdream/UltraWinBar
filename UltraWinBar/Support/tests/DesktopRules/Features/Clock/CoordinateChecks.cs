using System;
using System.Collections.Generic;
using System.Linq;
using UltraWinBar.Utilities;

// Coordinates typed or pasted in almost any form: which numbers they are, and which of them is the latitude.
internal static class CoordinateChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        var places = PlaceDatabase.Shared;

        void Expect(string text, string country, double latitude, double longitude, CoordinateOrderBasis? basis = null)
        {
            var result = CoordinateParser.Parse(text, country, places);
            if (!result.Success || Math.Abs(result.Latitude - latitude) > 1e-4 || Math.Abs(result.Longitude - longitude) > 1e-4)
                throw new Exception($"'{text}' ({country}): expected {latitude}, {longitude}, got {result.Problem} {result.Latitude}, {result.Longitude}.");
            if (basis.HasValue && result.Basis != basis.Value)
                throw new Exception($"'{text}' ({country}): order found by {result.Basis}, expected {basis}.");
        }
        void ExpectProblem(string text, string country, CoordinateProblem problem)
        {
            var result = CoordinateParser.Parse(text, country, places);
            if (result.Problem != problem) throw new Exception($"'{text}': expected {problem}, got {result.Problem}.");
        }

        // The same point in Tel Aviv, written many ways: separators, decimal marks, brackets, labels, hemispheres.
        foreach (string text in new[]
        {
            "32.0853, 34.7818", "32.0853,34.7818", "32.0853 34.7818", "32.0853\t34.7818", "32.0853\u00A034.7818",
            "32,0853 34,7818", "32,0853, 34,7818", "32,0853,34,7818", "32,0853;34,7818", "32.0853;34.7818",
            "32.0853:34.7818", "32.0853 : 34.7818", "32.0853/34.7818", "32.0853 | 34.7818", "32,0853 / 34,7818",
            "(32.0853, 34.7818)", "[32.0853,34.7818]", "{32.0853; 34.7818}", "  32.0853  ,  34.7818  ", "+32.0853, +34.7818",
            "32.0853°, 34.7818°", "32.0853° 34.7818°", "Coordinates: 32.0853, 34.7818", "Tel Aviv 32.0853 34.7818",
            "lat 32.0853 lon 34.7818", "lat: 32.0853, lng: 34.7818", "latitude=32.0853 longitude=34.7818", "LAT 32,0853 LON 34,7818",
            "N 32.0853 E 34.7818", "N32.0853 E34.7818", "32.0853N 34.7818E", "32.0853 N, 34.7818 E", "32.0853° N, 34.7818° E",
            "n 32.0853 e 34.7818", "32.0853N, E34.7818", "32.0853 N 34.7818 E",
            "32.0853 С, 34.7818 В", "С 32,0853 В 34,7818", "широта 32,0853 долгота 34,7818", "32,0853 шир 34,7818 долг",
            "32°05'07.08\"N 34°46'54.48\"E", "32° 5' 7.08\" N, 34° 46' 54.48\" E", "32°05′07.08″N 34°46′54.48″E",
            "32°05.118'N 34°46.908'E", "32º5'7,08''N 34º46'54,48''E", "N 32°05'07.08\" E 34°46'54.48\"",
        })
            Expect(text, "IL", 32.0853, 34.7818);

        // The order the other way round: the labels say so, or the country does.
        foreach (string text in new[]
        {
            "34.7818, 32.0853", "34,7818 32,0853", "34.7818:32.0853", "(34.7818; 32.0853)", "34,7818;32,0853",
            "lon 34.7818 lat 32.0853", "lng: 34.7818, lat: 32.0853", "34.7818 E 32.0853 N", "E34.7818 N32.0853", "34.7818E, 32.0853N",
            "34,7818 В, 32,0853 С", "долгота 34,7818 широта 32,0853", "34°46'54.48\"E 32°05'07.08\"N", "34.7818 lon 32.0853 lat",
        })
            Expect(text, "IL", 32.0853, 34.7818);
        Expect("34.7818, 32.0853", "IL", 32.0853, 34.7818, CoordinateOrderBasis.ByCountry);
        Expect("32.0853, 34.7818", "IL", 32.0853, 34.7818, CoordinateOrderBasis.ByCountry);
        Expect("N 32.0853 E 34.7818", "IL", 32.0853, 34.7818, CoordinateOrderBasis.Labeled);
        Expect("32.0853 34.7818", "IL", 32.0853, 34.7818);

        // The signs: minus, Unicode minus, hemisphere letters; and a hemisphere letter wins over a sign.
        foreach (string text in new[]
        {
            "-33.8688, 151.2093", "-33.8688 151.2093", "-33,8688 151,2093", "−33.8688, 151.2093", "–33.8688 151.2093",
            "33.8688 S 151.2093 E", "S 33.8688 E 151.2093", "33°52'7.68\"S 151°12'33.48\"E", "151.2093 -33.8688", "151.2093, 33.8688 S",
            "-33.8688 S 151.2093 E", "33,8688 Ю 151,2093 В",
        })
            Expect(text, "AU", -33.8688, 151.2093);
        foreach (string text in new[] { "40.7128, -74.0060", "-74.0060 40.7128", "40,7128 -74,0060", "40.7128 N 74.0060 W", "W 74.0060 N 40.7128", "40.7128N, 74.0060W", "40,7128 С 74,0060 З" })
            Expect(text, "US", 40.7128, -74.0060);
        // The antimeridian side of the world, the south-west and the far north.
        Expect("-41.2866 174.7756", "NZ", -41.2866, 174.7756);
        Expect("174.7756 -41.2866", "NZ", -41.2866, 174.7756);
        Expect("-34.6037 -58.3816", "AR", -34.6037, -58.3816);
        Expect("-58.3816 -34.6037", "AR", -34.6037, -58.3816);
        Expect("64.1466 -21.9426", "IS", 64.1466, -21.9426);
        Expect("-21.9426 64.1466", "IS", 64.1466, -21.9426);

        // Whole numbers: "32, 34" is two, and so is "32,34", the only way to read it as a pair.
        Expect("32, 34", "IL", 32, 34);
        Expect("32 34", "IL", 32, 34);
        Expect("32,34", "IL", 32, 34);
        Expect("32;34", "IL", 32, 34);

        // Only one order gives a latitude within 90 degrees, with or without a country.
        Expect("-118.2437 34.0522", null, 34.0522, -118.2437, CoordinateOrderBasis.OnlyOneFits);
        Expect("151.2093, -33.8688", null, -33.8688, 151.2093, CoordinateOrderBasis.OnlyOneFits);
        Expect("91 10", "IL", 10, 91, CoordinateOrderBasis.OnlyOneFits);

        // Without a country, or where the country cannot tell them apart, latitude comes first.
        Expect("32.0853, 34.7818", null, 32.0853, 34.7818, CoordinateOrderBasis.Assumed);
        Expect("34.7818, 32.0853", null, 34.7818, 32.0853, CoordinateOrderBasis.Assumed);
        var onTheDiagonal = CoordinateParser.Parse("0, 0", "GH", places);
        if (!onTheDiagonal.Success || onTheDiagonal.Basis != CoordinateOrderBasis.Assumed || onTheDiagonal.Warning != CoordinateWarning.OrderAssumed)
            throw new Exception("A point that fits a country the same in both orders must be read latitude first, with a warning.");

        // A point far from the country is accepted, with a warning that names how far.
        var farAway = CoordinateParser.Parse("35.6762 139.6503", "IL", places);
        if (!farAway.Success || farAway.Warning != CoordinateWarning.FarFromCountry || farAway.DistanceToCountryKm < 7000 || farAway.DistanceToCountryKm > 10000)
            throw new Exception($"Tokyo for Israel: expected a far-from-country warning near 9000 km, got {farAway.Warning} {farAway.DistanceToCountryKm:0}.");
        var nearby = CoordinateParser.Parse("32.0853 34.7818", "IL", places);
        if (nearby.Warning != CoordinateWarning.None || nearby.DistanceToCountryKm > 10)
            throw new Exception($"Tel Aviv for Israel: no warning expected, got {nearby.Warning} at {nearby.DistanceToCountryKm:0} km.");

        // What cannot be a pair of coordinates says why.
        ExpectProblem("", "IL", CoordinateProblem.Empty);
        ExpectProblem("   ", "IL", CoordinateProblem.Empty);
        ExpectProblem("hello", "IL", CoordinateProblem.NeedTwoNumbers);
        ExpectProblem("32.0853", "IL", CoordinateProblem.NeedTwoNumbers);
        ExpectProblem("32.0853 N", "IL", CoordinateProblem.NeedTwoNumbers);
        ExpectProblem("1 2 3", "IL", CoordinateProblem.TooManyNumbers);
        ExpectProblem("32.0853, 34.7818, 100", "IL", CoordinateProblem.TooManyNumbers);
        ExpectProblem("32:05:07 34:46:54", "IL", CoordinateProblem.TooManyNumbers);
        ExpectProblem("95 200", "IL", CoordinateProblem.NeitherOrderFits);
        ExpectProblem("-100 -190", "IL", CoordinateProblem.NeitherOrderFits);
        ExpectProblem("32 N 34 N", "IL", CoordinateProblem.TwoLatitudes);
        ExpectProblem("lat 32 lat 34", "IL", CoordinateProblem.TwoLatitudes);
        ExpectProblem("32 E 34 W", "IL", CoordinateProblem.TwoLongitudes);
        ExpectProblem("95 N 34 E", "IL", CoordinateProblem.LatitudeOutOfRange);
        ExpectProblem("32 N 190 E", "IL", CoordinateProblem.LongitudeOutOfRange);
        if (CoordinateParser.Parse(null, "IL", places).Problem != CoordinateProblem.Empty)
            throw new Exception("Null text is empty text.");
        // Garbage in all sorts of shapes must not throw.
        foreach (string garbage in new[] { "°°°", "''\"\"", ".", ",", "-", "+-+-", "1e5 2e5", "N E", "٣٢٫٠٨ ٣٤٫٧٨", "\uD800 1 2", "99999999999999999999 1", "1,,2", "1..2", "--1 --2", "0x10 0x20" })
            CoordinateParser.Parse(garbage, "IL", places);
        Console.WriteLine("PASS: coordinates in about a hundred written forms (dot or comma decimals; comma, semicolon, colon, slash, bar, space or tab between; brackets, labels, N/S/E/W and С/Ю/В/З, degrees-minutes-seconds, Unicode signs) give the same point, and every kind of bad input names its problem.");

        // The order is found from the country for real places: every 40th place of the data, written longitude first
        // with comma decimals and a separator that changes from one to the next, must come back as it was.
        var separators = new[] { " ", ", ", ";", ":", " / ", "	" };
        int checkedPlaces = 0, ambiguous = 0, resolvedByCountry = 0;
        for (int i = 0; i < places.Count; i += 40)
        {
            Place place = places[i];
            if (string.IsNullOrEmpty(place.CountryCode)) continue;
            string separator = separators[(i / 40) % separators.Length];
            string longitude = place.Longitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            string latitude = place.Latitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            if ((i / 40) % 2 == 0) { longitude = longitude.Replace('.', ','); latitude = latitude.Replace('.', ','); }
            string swapped = longitude + separator + latitude;
            var result = CoordinateParser.Parse(swapped, place.CountryCode, places);
            checkedPlaces++;
            if (!result.Success) throw new Exception($"'{swapped}' ({place.Display}): {result.Problem}.");
            // When the longitude is above 90 only one order is possible. Otherwise the wrong order must land
            // clearly farther from the country than the place itself, or the two cannot be told apart.
            bool bothFit = Math.Abs(place.Longitude) <= 90;
            if (bothFit && places.TryDistanceToCountry(place.CountryCode, place.Longitude, place.Latitude, out double wrongOrderKm) &&
                wrongOrderKm < CoordinateParser.DecisiveGapKm)
            {
                ambiguous++;
                continue;
            }
            if (Math.Abs(result.Latitude - place.Latitude) > 1e-3 || Math.Abs(result.Longitude - place.Longitude) > 1e-3)
                throw new Exception($"'{swapped}' for {place.Display} ({place.CountryCode}): read as {result.Latitude}, {result.Longitude} by {result.Basis}.");
            if (result.Basis == CoordinateOrderBasis.ByCountry) resolvedByCountry++;
        }
        if (checkedPlaces < 1000 || ambiguous > checkedPlaces / 20 || resolvedByCountry < checkedPlaces / 3)
            throw new Exception($"Too few places read back by the country: checked {checkedPlaces}, ambiguous {ambiguous}, by country {resolvedByCountry}.");
        Console.WriteLine($"PASS: {checkedPlaces} places of the data, written longitude first, are read back by their country ({resolvedByCountry} told apart by the country, {ambiguous} too close to call).");

        if (CoordinateParser.Format(32.0853, 34.7818) != "32.0853° N, 34.7818° E" ||
            CoordinateParser.Format(-33.8688, 151.2093) != "33.8688° S, 151.2093° E" ||
            CoordinateParser.Format(40.7128, -74.006) != "40.7128° N, 74.006° W" ||
            CoordinateParser.Format(0, 0) != "0° N, 0° E")
            throw new Exception("Coordinates are written with hemisphere letters and no more than four decimals.");
        foreach (var (latitude, longitude) in new[] { (32.0853, 34.7818), (-33.8688, 151.2093), (40.7128, -74.006), (-54.8019, -68.303), (64.1466, -21.9426) })
        {
            var back = CoordinateParser.Parse(CoordinateParser.Format(latitude, longitude), null, places);
            if (!back.Success || back.Basis != CoordinateOrderBasis.Labeled || Math.Abs(back.Latitude - latitude) > 1e-4 || Math.Abs(back.Longitude - longitude) > 1e-4)
                throw new Exception($"Format must be read back exactly: {latitude}, {longitude}.");
        }
        Console.WriteLine("PASS: formatted coordinates read back as they were.");
    }
}
