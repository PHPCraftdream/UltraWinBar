using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace UltraWinBar.Utilities
{
    /// <summary>A country of the place data: its ISO 3166 alpha-2 code and its English name.</summary>
    public sealed record CountryInfo(string Code, string Name);

    /// <summary>A populated place of the place data; TimeZoneId is its IANA time zone ("Asia/Jerusalem"), empty if unknown.</summary>
    public sealed record Place(string Name, string Region, string Country, string CountryCode,
        double Latitude, double Longitude, int Population, string TimeZoneId = "")
    {
        /// <summary>"Paris, Texas, United States": the name, then the parts that are known.</summary>
        public string Display => string.Join(", ", new[] { Name, Region, Country }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>
    /// The cities of the world with at least 10 000 inhabitants (GeoNames, CC BY 4.0), read once from the
    /// embedded resource into arrays: what a place is called, where it lies, how big it is. The text format
    /// is described in Assets/Resources/Places/GeoNames-NOTICE.txt. Immutable once loaded.
    /// </summary>
    public sealed class PlaceDatabase
    {
        public const string ResourceName = "UltraWinBar.Resources.Places.tsv.gz";

        private static readonly Lazy<PlaceDatabase> shared = new Lazy<PlaceDatabase>(LoadResource);

        /// <summary>The place data of the program, loaded on first use (about 50 ms).</summary>
        public static PlaceDatabase Shared => shared.Value;

        private readonly string[] names;
        private readonly string[] foldedNames;
        private readonly int[] regionOf;       // -1: none
        private readonly int[] countryOf;      // -1: none
        private readonly int[] zoneOf;         // -1: none
        private readonly string[] zoneNames;
        private readonly double[] latitudes;
        private readonly double[] longitudes;
        private readonly int[] populations;
        private readonly string[] regionNames;
        private readonly string[] foldedRegions;
        private readonly CountryInfo[] countries;
        private readonly string[] foldedCountries;
        private readonly int[][] placesOfCountry;
        private readonly Dictionary<string, int> countryByCode;

        private PlaceDatabase(string[] names, int[] regionOf, int[] countryOf, int[] zoneOf, double[] latitudes, double[] longitudes,
            int[] populations, string[] regionNames, CountryInfo[] countries, string[] zoneNames)
        {
            this.zoneOf = zoneOf;
            this.zoneNames = zoneNames;
            this.names = names;
            this.regionOf = regionOf;
            this.countryOf = countryOf;
            this.latitudes = latitudes;
            this.longitudes = longitudes;
            this.populations = populations;
            this.regionNames = regionNames;
            this.countries = countries;
            foldedNames = names.Select(Fold).ToArray();
            foldedRegions = regionNames.Select(Fold).ToArray();
            foldedCountries = countries.Select(country => Fold(country.Name)).ToArray();
            countryByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < countries.Length; i++) countryByCode[countries[i].Code] = i;
            var lists = countries.Select(_ => new List<int>()).ToArray();
            for (int i = 0; i < names.Length; i++)
                if (countryOf[i] >= 0) lists[countryOf[i]].Add(i);
            placesOfCountry = lists.Select(list => list.ToArray()).ToArray();
        }

        public int Count => names.Length;

        /// <summary>The countries that have places, by name.</summary>
        public IReadOnlyList<CountryInfo> Countries => countries;

        /// <summary>The IANA time zones the places are in, by name.</summary>
        public IReadOnlyList<string> TimeZones => zoneNames;

        public CountryInfo FindCountry(string code) =>
            !string.IsNullOrEmpty(code) && countryByCode.TryGetValue(code, out int index) ? countries[index] : null;

        public Place this[int index] => new Place(names[index],
            regionOf[index] >= 0 ? regionNames[regionOf[index]] : "",
            countryOf[index] >= 0 ? countries[countryOf[index]].Name : "",
            countryOf[index] >= 0 ? countries[countryOf[index]].Code : "",
            latitudes[index], longitudes[index], populations[index], zoneOf[index] >= 0 ? zoneNames[zoneOf[index]] : "");

        public IEnumerable<Place> PlacesOf(string countryCode)
        {
            if (string.IsNullOrEmpty(countryCode) || !countryByCode.TryGetValue(countryCode, out int country)) yield break;
            foreach (int index in placesOfCountry[country]) yield return this[index];
        }

        /// <summary>
        /// The places whose names, regions or countries start with every word of the query, in any order
        /// ("saint pet", "texas paris"): the places named as asked first, then those whose name starts as asked,
        /// then the rest; the bigger first within each. Accents and case do not matter.
        /// </summary>
        public IReadOnlyList<Place> Search(string query, int limit = 50)
        {
            string[] words = Fold(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || limit <= 0) return Array.Empty<Place>();
            string whole = string.Join(" ", words);
            var found = new List<(int Index, int Rank)>();
            for (int i = 0; i < names.Length; i++)
            {
                bool inName = true;
                bool matches = true;
                foreach (string word in words)
                {
                    if (StartsAWord(foldedNames[i], word)) continue;
                    inName = false;
                    if ((regionOf[i] >= 0 && StartsAWord(foldedRegions[regionOf[i]], word)) ||
                        (countryOf[i] >= 0 && StartsAWord(foldedCountries[countryOf[i]], word))) continue;
                    matches = false;
                    break;
                }
                if (!matches) continue;
                int rank = foldedNames[i] == whole ? 0
                    : inName && foldedNames[i].StartsWith(whole, StringComparison.Ordinal) ? 1
                    : inName ? 2 : 3;
                found.Add((i, rank));
            }
            found.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank)
                : populations[a.Index] != populations[b.Index] ? populations[b.Index].CompareTo(populations[a.Index])
                : a.Index.CompareTo(b.Index));
            return found.Take(limit).Select(item => this[item.Index]).ToArray();
        }

        /// <summary>
        /// How far the point is from the nearest known place of the country. False when the country is not
        /// known or has no places.
        /// </summary>
        public bool TryDistanceToCountry(string countryCode, double latitude, double longitude, out double distanceKm)
        {
            distanceKm = double.MaxValue;
            if (string.IsNullOrEmpty(countryCode) || !countryByCode.TryGetValue(countryCode, out int country)) return false;
            foreach (int index in placesOfCountry[country])
                distanceKm = Math.Min(distanceKm, DistanceKm(latitude, longitude, latitudes[index], longitudes[index]));
            return placesOfCountry[country].Length > 0;
        }

        /// <summary>
        /// The time zone of the nearest place that has one: of the country when a code is given, else of the world. Empty when
        /// there is none. A suggestion for typed-in coordinates; the user chooses.
        /// </summary>
        public string NearestTimeZone(string countryCode, double latitude, double longitude)
        {
            IEnumerable<int> candidates = !string.IsNullOrEmpty(countryCode) && countryByCode.TryGetValue(countryCode, out int country)
                ? placesOfCountry[country] : Enumerable.Range(0, names.Length);
            int best = -1;
            double bestDistance = double.MaxValue;
            foreach (int index in candidates)
            {
                if (zoneOf[index] < 0) continue;
                double distance = DistanceKm(latitude, longitude, latitudes[index], longitudes[index]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }
            return best < 0 ? "" : zoneNames[zoneOf[best]];
        }

        /// <summary>Great-circle distance on a sphere of the mean Earth radius, 6371 km.</summary>
        public static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
        {
            const double radians = Math.PI / 180;
            double dLat = (latitude2 - latitude1) * radians, dLng = (longitude2 - longitude1) * radians;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(latitude1 * radians) * Math.Cos(latitude2 * radians) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * 6371.0 * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
        }

        /// <summary>Lower case, without accents, words separated by single spaces: the form names are compared in.</summary>
        public static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var folded = new StringBuilder(text.Length);
            bool wordBreak = true;
            foreach (char letter in Decompose(text))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(letter) == UnicodeCategory.NonSpacingMark) continue;
                string plain = Plain(letter);
                if (plain != null)
                {
                    folded.Append(plain);
                    wordBreak = false;
                }
                else if (char.IsLetterOrDigit(letter))
                {
                    folded.Append(char.ToLowerInvariant(letter));
                    wordBreak = false;
                }
                else if (!wordBreak)
                {
                    folded.Append(' ');
                    wordBreak = true;
                }
            }
            return folded.ToString().TrimEnd();
        }

        private static string Decompose(string text)
        {
            try { return text.Normalize(NormalizationForm.FormD); }
            catch (ArgumentException) { return text; }   // a broken surrogate pair
        }

        // Letters that do not take apart into a letter and an accent.
        private static string Plain(char letter) => letter switch
        {
            'ø' or 'Ø' => "o",
            'ł' or 'Ł' => "l",
            'đ' or 'Đ' => "d",
            'ı' => "i",
            'ß' => "ss",
            'æ' or 'Æ' => "ae",
            'œ' or 'Œ' => "oe",
            _ => null,
        };

        private static bool StartsAWord(string text, string prefix)
        {
            for (int at = text.IndexOf(prefix, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(prefix, at + 1, StringComparison.Ordinal))
                if (at == 0 || text[at - 1] == ' ') return true;
            return false;
        }

        public static PlaceDatabase Load(Stream gzipped)
        {
            using var gzip = new GZipStream(gzipped, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return Parse(reader);
        }

        private static PlaceDatabase LoadResource()
        {
            using Stream resource = typeof(PlaceDatabase).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException("The place data is missing from the program.");
            return Load(resource);
        }

        public static PlaceDatabase Parse(TextReader reader)
        {
            string header = reader.ReadLine() ?? throw new InvalidDataException("The place data is empty.");
            string[] counts = header.Split('\t');
            if (counts.Length != 5 || counts[0] != "places v2")
                throw new InvalidDataException($"Unknown place data: '{header}'.");
            int placeCount = WholeNumber(counts[1], 1), countryCount = WholeNumber(counts[2], 1), regionCount = WholeNumber(counts[3], 1),
                zoneCount = WholeNumber(counts[4], 1);

            var countries = new List<CountryInfo>(countryCount);
            var regionNames = new List<string>(regionCount);
            var zoneNames = new List<string>(zoneCount);
            var zoneOf = new List<int>(placeCount);
            var names = new List<string>(placeCount);
            var regionOf = new List<int>(placeCount);
            var countryOf = new List<int>(placeCount);
            var latitudes = new List<double>(placeCount);
            var longitudes = new List<double>(placeCount);
            var populations = new List<int>(placeCount);

            int lineNumber = 1;
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                lineNumber++;
                if (line.Length == 0) continue;
                string[] fields = line.Split('\t');
                switch (fields[0])
                {
                    case "C" when fields.Length == 3:
                        countries.Add(new CountryInfo(fields[1], fields[2]));
                        break;
                    case "R" when fields.Length == 3:
                        WholeNumber(fields[1], lineNumber, countries.Count);
                        regionNames.Add(fields[2]);
                        break;
                    case "Z" when fields.Length == 2 && fields[1].Length > 0:
                        zoneNames.Add(fields[1]);
                        break;
                    case "P" when fields.Length == 8:
                        names.Add(fields[1]);
                        regionOf.Add(fields[2].Length == 0 ? -1 : WholeNumber(fields[2], lineNumber, regionNames.Count));
                        countryOf.Add(fields[3].Length == 0 ? -1 : WholeNumber(fields[3], lineNumber, countries.Count));
                        latitudes.Add(Coordinate(fields[4], lineNumber, -90, 90));
                        longitudes.Add(Coordinate(fields[5], lineNumber, -180, 180));
                        populations.Add(WholeNumber(fields[6], lineNumber));
                        zoneOf.Add(fields[7].Length == 0 ? -1 : WholeNumber(fields[7], lineNumber, zoneNames.Count));
                        break;
                    default:
                        throw new InvalidDataException($"Place data, line {lineNumber}: cannot read '{line}'.");
                }
            }
            if (names.Count != placeCount || countries.Count != countryCount || regionNames.Count != regionCount || zoneNames.Count != zoneCount)
                throw new InvalidDataException("The place data does not have as many records as its header says.");
            return new PlaceDatabase(names.ToArray(), regionOf.ToArray(), countryOf.ToArray(), zoneOf.ToArray(), latitudes.ToArray(),
                longitudes.ToArray(), populations.ToArray(), regionNames.ToArray(), countries.ToArray(), zoneNames.ToArray());
        }

        // A whole number, 0 or more and below "limit" when one is given.
        private static int WholeNumber(string text, int lineNumber, int limit = int.MaxValue)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value >= limit)
                throw new InvalidDataException($"Place data, line {lineNumber}: '{text}' is not a valid number.");
            return value;
        }

        private static double Coordinate(string text, int lineNumber, double min, double max)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < min || value > max)
                throw new InvalidDataException($"Place data, line {lineNumber}: '{text}' is not a valid coordinate.");
            return value;
        }
    }
}
