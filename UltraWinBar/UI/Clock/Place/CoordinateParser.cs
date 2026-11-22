using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltraWinBar.Utilities
{
    public enum CoordinateProblem
    {
        None,
        Empty,
        NeedTwoNumbers,
        TooManyNumbers,
        TwoLatitudes,
        TwoLongitudes,
        LatitudeOutOfRange,
        LongitudeOutOfRange,
        NeitherOrderFits,
    }

    /// <summary>How the parser knew which of the two numbers is the latitude.</summary>
    public enum CoordinateOrderBasis
    {
        /// <summary>The text says so: N/S/E/W (or С/Ю/В/З), "lat", "lon", "широта", "долгота".</summary>
        Labeled,
        /// <summary>Only one order gives a latitude within 90 degrees and a longitude within 180.</summary>
        OnlyOneFits,
        /// <summary>Both orders are possible; one lands much nearer to the places of the country.</summary>
        ByCountry,
        /// <summary>Both orders are possible and the country does not tell them apart: latitude first.</summary>
        Assumed,
    }

    public enum CoordinateWarning
    {
        None,
        /// <summary>The point is far from every place of the country: wrong country, or wrong numbers.</summary>
        FarFromCountry,
        /// <summary>Latitude first was assumed; longitude first would fit the country about as well.</summary>
        OrderAssumed,
    }

    public sealed record CoordinateParseResult(
        CoordinateProblem Problem,
        double Latitude,
        double Longitude,
        CoordinateOrderBasis Basis,
        CoordinateWarning Warning,
        double DistanceToCountryKm)
    {
        public bool Success => Problem == CoordinateProblem.None;

        internal static CoordinateParseResult Failure(CoordinateProblem problem) =>
            new CoordinateParseResult(problem, 0, 0, CoordinateOrderBasis.Assumed, CoordinateWarning.None, double.NaN);
    }

    /// <summary>
    /// Reads a pair of coordinates typed or pasted in almost any way and finds out which number is the latitude.
    ///
    /// The numbers: the fraction may follow a dot or a comma ("32.0853", "32,0853"); the two may be set apart by a
    /// comma, a semicolon, a colon, a slash, a space or a tab, in any mixture ("32,0853, 34,7818", "32.0853:34.7818");
    /// a sign may be a minus or a plus; brackets, labels and units around them are ignored. Degrees, minutes and
    /// seconds are read when they are marked: 32°05'07.2"N 34°46'54"E.
    ///
    /// The order: "N", "S", "E", "W" (and the Russian С, Ю, В, З) and the words "lat", "lon", "lng", "latitude",
    /// "longitude", "широта", "долгота" say which is which, before or after their number. Without them the order is
    /// found from the country: a latitude is within 90 degrees, so a number above that is a longitude; when both
    /// orders are possible, the one that lands nearer to the places of the country wins.
    /// </summary>
    public static class CoordinateParser
    {
        // Where the pair is far from the places of the country, in km, the user is warned.
        public const double FarFromCountryKm = 1500;
        // One order is "much nearer" when it is at least this much nearer, in km.
        public const double DecisiveGapKm = 100;

        private enum Role { None, Latitude, Longitude }

        private sealed class Item
        {
            public bool IsNumber;
            public double Value;
            public int Sign;            // marker: +1 / -1 for a hemisphere letter, 0 for a label
            public Role Role;           // marker: what it names; number: what a marker made of it
            public int HemisphereSign;  // number: +1 / -1 when a hemisphere letter was attached
            public bool WholeWithComma; // number: written as digits, a comma and digits ("32,34")
            public string WholeDigits;  // ... the digits before the comma
            public string FractionDigits; // ... and after it
        }

        public static CoordinateParseResult Parse(string text, string countryCode = null, PlaceDatabase places = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return CoordinateParseResult.Failure(CoordinateProblem.Empty);

            List<Item> items = Scan(Normalize(text));
            Attach(items);
            List<Item> numbers = items.Where(item => item.IsNumber).ToList();
            if (numbers.Count == 1 && numbers[0].WholeWithComma) numbers = SplitWholeNumbers(numbers[0]);
            if (numbers.Count < 2) return CoordinateParseResult.Failure(CoordinateProblem.NeedTwoNumbers);
            if (numbers.Count > 2) return CoordinateParseResult.Failure(CoordinateProblem.TooManyNumbers);

            double first = Signed(numbers[0]), second = Signed(numbers[1]);
            Role firstRole = numbers[0].Role, secondRole = numbers[1].Role;
            if (firstRole != Role.None || secondRole != Role.None)
            {
                if (firstRole == Role.None) firstRole = secondRole == Role.Latitude ? Role.Longitude : Role.Latitude;
                if (secondRole == Role.None) secondRole = firstRole == Role.Latitude ? Role.Longitude : Role.Latitude;
                if (firstRole == secondRole)
                    return CoordinateParseResult.Failure(firstRole == Role.Latitude ? CoordinateProblem.TwoLatitudes : CoordinateProblem.TwoLongitudes);
                double latitude = firstRole == Role.Latitude ? first : second, longitude = firstRole == Role.Latitude ? second : first;
                if (Math.Abs(latitude) > 90) return CoordinateParseResult.Failure(CoordinateProblem.LatitudeOutOfRange);
                if (Math.Abs(longitude) > 180) return CoordinateParseResult.Failure(CoordinateProblem.LongitudeOutOfRange);
                return Result(latitude, longitude, CoordinateOrderBasis.Labeled, countryCode, places, false);
            }

            bool latitudeFirstFits = Math.Abs(first) <= 90 && Math.Abs(second) <= 180;
            bool longitudeFirstFits = Math.Abs(second) <= 90 && Math.Abs(first) <= 180;
            if (!latitudeFirstFits && !longitudeFirstFits)
                return CoordinateParseResult.Failure(CoordinateProblem.NeitherOrderFits);
            if (!longitudeFirstFits) return Result(first, second, CoordinateOrderBasis.OnlyOneFits, countryCode, places, false);
            if (!latitudeFirstFits) return Result(second, first, CoordinateOrderBasis.OnlyOneFits, countryCode, places, false);

            if (places != null &&
                places.TryDistanceToCountry(countryCode, first, second, out double asGiven) &&
                places.TryDistanceToCountry(countryCode, second, first, out double swapped))
            {
                bool decisive = Math.Abs(asGiven - swapped) >= DecisiveGapKm;
                if (decisive && swapped < asGiven) return Result(second, first, CoordinateOrderBasis.ByCountry, countryCode, places, false);
                if (decisive) return Result(first, second, CoordinateOrderBasis.ByCountry, countryCode, places, false);
                return Result(first, second, CoordinateOrderBasis.Assumed, countryCode, places, true);
            }
            return Result(first, second, CoordinateOrderBasis.Assumed, countryCode, places, false);
        }

        /// <summary>"32.0853° N, 34.7818° E".</summary>
        public static string Format(double latitude, double longitude) => string.Format(CultureInfo.InvariantCulture,
            "{0:0.####}° {1}, {2:0.####}° {3}", Math.Abs(latitude), latitude < 0 ? "S" : "N", Math.Abs(longitude), longitude < 0 ? "W" : "E");

        private static CoordinateParseResult Result(double latitude, double longitude, CoordinateOrderBasis basis,
            string countryCode, PlaceDatabase places, bool orderAmbiguous)
        {
            double distance = double.NaN;
            var warning = orderAmbiguous ? CoordinateWarning.OrderAssumed : CoordinateWarning.None;
            if (places != null && places.TryDistanceToCountry(countryCode, latitude, longitude, out double km))
            {
                distance = km;
                if (km > FarFromCountryKm) warning = CoordinateWarning.FarFromCountry;
            }
            return new CoordinateParseResult(CoordinateProblem.None, latitude, longitude, basis, warning, distance);
        }

        private static double Signed(Item number) => number.HemisphereSign != 0 ? number.HemisphereSign * Math.Abs(number.Value) : number.Value;

        // "32,34" is one number as written and two as meant: nothing else can make a pair of it. A minus belongs
        // to the first.
        private static List<Item> SplitWholeNumbers(Item number)
        {
            var pair = new List<Item>();
            if (double.TryParse(number.WholeDigits, NumberStyles.None, CultureInfo.InvariantCulture, out double whole) &&
                double.TryParse(number.FractionDigits, NumberStyles.None, CultureInfo.InvariantCulture, out double part))
            {
                pair.Add(new Item { IsNumber = true, Value = (number.Value < 0 ? -1 : 1) * whole });
                pair.Add(new Item { IsNumber = true, Value = part });
            }
            return pair;
        }

        private const char Degree = '°', Minute = '′', Second = '″';

        // Unicode look-alikes made plain: minus signs, spaces, degree, minute and second marks.
        private static string Normalize(string text)
        {
            var plain = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '−': case '–': case '—': case '﹣': case '－': plain.Append('-'); break;
                    case 'º': case '˚': case '∘': plain.Append(Degree); break;
                    case '\'': case '’': case '′': case '´': case '`': case 'ʹ': plain.Append(Minute); break;
                    case '"': case '”': case '“': case '″': case 'ʺ': plain.Append(Second); break;
                    default: plain.Append(char.IsWhiteSpace(c) ? ' ' : c); break;
                }
            }
            // Two minute marks in a row are a second mark.
            return plain.ToString().Replace(new string(Minute, 2), Second.ToString());
        }

        private static List<Item> Scan(string text)
        {
            var items = new List<Item>();
            int at = 0;
            while (at < text.Length)
            {
                char c = text[at];
                bool hasSign = (c == '+' || c == '-') && at + 1 < text.Length && StartsNumber(text, at + 1);
                if (hasSign || StartsNumber(text, at))
                {
                    int sign = hasSign && c == '-' ? -1 : 1;
                    if (hasSign) at++;
                    items.Add(ReadNumber(text, ref at, sign));
                }
                else if (char.IsLetter(c))
                {
                    int start = at;
                    while (at < text.Length && char.IsLetter(text[at])) at++;
                    Item marker = Marker(text.Substring(start, at - start));
                    if (marker != null) items.Add(marker);
                }
                else at++;
            }
            return items;
        }

        // Only 0-9: char.IsDigit also accepts the digits of other scripts, which double.Parse does not.
        private static bool Digit(char c) => c >= '0' && c <= '9';

        // A number starts with a digit, or with a dot that does not follow a digit and is followed by one.
        private static bool StartsNumber(string text, int at) =>
            Digit(text[at]) ||
            (text[at] == '.' && at + 1 < text.Length && Digit(text[at + 1]) && (at == 0 || !Digit(text[at - 1])));

        // Digits with a fraction after a dot or a comma (a comma only when digits follow it), then degrees, minutes
        // and seconds when their marks follow.
        private static Item ReadNumber(string text, ref int at, int sign)
        {
            ReadDecimal(text, ref at, out double degrees, out bool comma, out string whole, out string fraction);
            int look = SkipSpaces(text, at);
            if (look < text.Length && text[look] == Degree)
            {
                at = look + 1;
                double value = degrees + MarkedPart(text, ref at, Minute) / 60;
                value += MarkedPart(text, ref at, Second) / 3600;
                return new Item { IsNumber = true, Value = sign * value };
            }
            return new Item
            {
                IsNumber = true,
                Value = sign * degrees,
                WholeWithComma = comma && fraction.Length > 0 && whole.Length > 0,
                WholeDigits = whole,
                FractionDigits = fraction,
            };
        }

        // A number followed by the mark: minutes or seconds. Zero, with the position kept, when it is not there.
        private static double MarkedPart(string text, ref int at, char mark)
        {
            int start = SkipSpaces(text, at);
            if (start >= text.Length || !StartsNumber(text, start)) return 0;
            int end = start;
            ReadDecimal(text, ref end, out double value, out _, out _, out _);
            int markAt = SkipSpaces(text, end);
            if (markAt >= text.Length || text[markAt] != mark) return 0;
            at = markAt + 1;
            return value;
        }

        // Digits, and a fraction after a dot or a comma when digits follow it.
        private static void ReadDecimal(string text, ref int at, out double value, out bool comma, out string whole, out string fraction)
        {
            int start = at;
            while (at < text.Length && Digit(text[at])) at++;
            whole = text.Substring(start, at - start);
            fraction = "";
            comma = false;
            if (at + 1 < text.Length && (text[at] == '.' || text[at] == ',') && Digit(text[at + 1]))
            {
                comma = text[at] == ',';
                int fractionStart = ++at;
                while (at < text.Length && Digit(text[at])) at++;
                fraction = text.Substring(fractionStart, at - fractionStart);
            }
            value = double.Parse((whole.Length == 0 ? "0" : whole) + "." + (fraction.Length == 0 ? "0" : fraction),
                NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int SkipSpaces(string text, int at)
        {
            while (at < text.Length && text[at] == ' ') at++;
            return at;
        }

        // A hemisphere letter or a word that names the latitude or the longitude; anything else is not a marker.
        private static Item Marker(string word)
        {
            string lower = word.ToLowerInvariant();
            if (word.Length == 1)
            {
                switch (lower[0])
                {
                    case 'n': case 'с': case 'н': return new Item { Role = Role.Latitude, Sign = 1 };    // N, С, Н
                    case 's': case 'ю': return new Item { Role = Role.Latitude, Sign = -1 };                  // S, Ю
                    case 'e': case 'в': case 'е': return new Item { Role = Role.Longitude, Sign = 1 };   // E, В, Е
                    case 'w': case 'з': return new Item { Role = Role.Longitude, Sign = -1 };                 // W, З
                    default: return null;
                }
            }
            switch (lower)
            {
                case "lat": case "latitude": case "шир": case "широта":
                    return new Item { Role = Role.Latitude };
                case "lon": case "long": case "lng": case "longitude": case "долг": case "долгота":
                    return new Item { Role = Role.Longitude };
                default:
                    return null;
            }
        }

        // Each marker names the number after it or the number before it: after it when the text begins with a marker
        // ("N 32.08 E 34.78"), before it otherwise ("32.08 N 34.78 E"); a number that has a marker already is not taken.
        private static void Attach(List<Item> items)
        {
            bool markersFirst = items.Count > 0 && !items[0].IsNumber;
            for (int i = 0; i < items.Count; i++)
            {
                Item marker = items[i];
                if (marker.IsNumber) continue;
                Item before = Neighbour(items, i, -1), after = Neighbour(items, i, 1);
                Item target = before != null && after != null ? (markersFirst ? after : before) : before ?? after;
                if (target == null) continue;
                target.Role = marker.Role;
                target.HemisphereSign = marker.Sign;
            }
        }

        // The nearest number in the direction that has no marker yet, with no other number between.
        private static Item Neighbour(List<Item> items, int from, int step)
        {
            for (int i = from + step; i >= 0 && i < items.Count; i += step)
            {
                if (!items[i].IsNumber) continue;
                return items[i].Role == Role.None ? items[i] : null;
            }
            return null;
        }
    }
}
