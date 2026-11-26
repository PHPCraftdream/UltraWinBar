using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>A line of the times of a day: what it is and when ("Sunrise (Netz)", "06:02").</summary>
    /// <summary>
    /// A time of the day; Rest marks the lighting of the candles or the havdalah of a day of rest, Chanukah the lighting of
    /// the candles of Hanukkah.
    /// </summary>
    public sealed record TimeLine(string Name, string Time, bool Rest = false, bool Chanukah = false);

    /// <summary>What the calendar says about a day, ready to show: every text is in the language of the settings.</summary>
    public sealed record DayDetails(string Heading, string HebrewDate, string Portion, IReadOnlyList<string> Events,
        IReadOnlyList<string> Yahrzeits, string Molad, string Place, IReadOnlyList<TimeLine> Times, string Rest, int Chanukah = 0);

    /// <summary>A day of the month grid: its numbers, the one thing worth showing in the cell, and what it says when pointed at.</summary>
    public sealed record DayCell(DateTime Date, string GregorianNumber, string HebrewNumber, string Marker, bool OutsideMonth,
        bool IsToday, bool IsRest, bool HasYahrzeit, string Tooltip, string HebrewLabel = "", string GregorianLabel = "",
        string Portion = null, IReadOnlyList<CellEvent> Events = null, bool EveningEnds = false, bool EveningBegins = false,
        int Omer = 0, string OmerText = null, int Chanukah = 0)
    {
        public bool HasOmer => Omer > 0;
        public bool HasChanukah => Chanukah > 0;
    }

    /// <summary>A festival, fast, minor day or Rosh Chodesh in a cell, with its kind (the color of its block).</summary>
    public sealed record CellEvent(string Name, DayKind Kind);

    /// <summary>
    /// Puts together what the calendar window shows: from the date, the place (or none) and the settings it makes the texts of
    /// the grid cell and of the details of a day. The words around the names come from <c>text</c>, the strings of the program.
    /// </summary>
    public static class DayDetailsBuilder
    {
        /// <summary>The country codes of the place data that are the Land of Israel, where the festivals last a day less.</summary>
        public static bool IsLandOfIsrael(HebrewPlace place) => place != null && (place.CountryCode == "IL" || place.CountryCode == "PS");

        /// <summary>
        /// The marks by the clock's weekday for a Hebrew day: whether it has a yahrzeit that is shown (not hidden), and its day
        /// of the Omer (0 outside it).
        /// </summary>
        public static (bool Yahrzeit, int Omer) MarksOf(DateTime hebrewDay, ICollection<string> hidden) =>
            (Yahrzeits.On(hebrewDay, hidden).Count > 0, JewishCalendar.OmerDay(hebrewDay));

        /// <summary>
        /// The candles of Hanukkah lit on the evening of a civil date, besides the shamash (0 when none): the evening of
        /// 24 Kislev has 1, so a Friday has its candles before the Shabbat comes in.
        /// </summary>
        public static int ChanukahTonight(DateTime civilDate) => JewishCalendar.ChanukahCandles(civilDate.Date.AddDays(1));

        /// <summary>The day of the Omer as a line: "Day 23 of the Omer — Netzach she'b'Tiferet"; null outside it.</summary>
        public static string OmerLine(int day, string language, Func<string, string> text) =>
            day > 0 ? string.Format(text("calendar_omer_day"), day) + " — " + JewishCalendar.OmerSefira(day, language) : null;

        public static CultureInfo CultureOf(string language)
        {
            try
            {
                return language == "русский" ? new CultureInfo("ru-RU") : language == "עברית" ? new CultureInfo("he-IL")
                    : language == "English" ? new CultureInfo("en-US") : CultureInfo.CurrentCulture;
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.CurrentCulture;
            }
        }

        // A day of the Hebrew calendar as the name of the day with its number in the festival ("Hanukkah, day 3 of 8").
        private static string EventName(JewishDay day, string language, Func<string, string> text) =>
            day.Count > 1 ? string.Format(text("calendar_day_of"), day.Name(language), day.Number, day.Count) : day.Name(language);

        public static DayCell Cell(DateTime date, DateTime month, DateTime today, bool israel, string language, ICollection<string> hidden, Func<string, string> text)
        {
            var events = JewishCalendar.Days(date.Date, israel);
            var people = Yahrzeits.On(date, hidden);
            TorahReading reading = date.DayOfWeek == DayOfWeek.Saturday ? TorahReadings.ForShabbat(date, israel) : null;
            string marker = events.Count > 0 ? EventName(events.OrderBy(day => day.Kind == DayKind.RoshChodesh ? 1 : 0).First(), language, text)
                : reading != null ? reading.Name(language) : "";
            var lines = events.Select(day => EventName(day, language, text)).ToList();
            if (reading != null) lines.Insert(0, string.Format(text("calendar_portion"), reading.Name(language)));
            lines.AddRange(people.Select(person => person.Name(language)));
            string hebrew = HebrewClockFormatter.FormatDate(date, language);
            string tooltip = string.Join(Environment.NewLine, new[] { date.ToString("d MMMM yyyy", CultureOf(language)), hebrew }.Concat(lines));
            int omer = JewishCalendar.OmerDay(date);
            string number = hebrew.Length >= 2 ? hebrew.Substring(0, 2) : "";
            // The cell shows the Hebrew date at its top right ("Kislev, 13") and the civil date at its bottom left ("23, November").
            CultureInfo culture = CultureOf(language);
            string civilMonth = culture.DateTimeFormat.MonthGenitiveNames[date.Month - 1];
            string civil = date.Day.ToString(CultureInfo.InvariantCulture) + ", " + (string.IsNullOrEmpty(civilMonth) ? culture.DateTimeFormat.GetMonthName(date.Month) : civilMonth);
            return new DayCell(date.Date, date.Day.ToString(CultureInfo.InvariantCulture), number, marker,
                date.Month != month.Month, date.Date == today.Date, Zmanim.IsRestDay(date, israel), people.Count > 0, tooltip,
                HebrewClockFormatter.FormatMonthDay(date, language), civil,
                // The portion of a Shabbat, unless a festival's day is read instead; the days, festivals first.
                reading != null && !events.Any(day => day.Kind == DayKind.YomTov || day.Kind == DayKind.CholHamoed) ? reading.Name(language) : null,
                events.OrderBy(day => day.Kind == DayKind.RoshChodesh ? 1 : 0).Select(day => new CellEvent(EventName(day, language, text), day.Kind)).ToList(),
                Omer: omer, OmerText: OmerLine(omer, language, text), Chanukah: ChanukahTonight(date));
        }

        // The time as the clocks of the place show it: in its zone, or the computer's when none was chosen.
        private static DateTime? OnClock(DateTime? utc, HebrewPlace place)
        {
            if (!utc.HasValue) return null;
            return ZoneClock.Now(place.TimeZoneId, utc.Value) ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), TimeZoneInfo.Local);
        }

        private static string Short(DateTime? moment, CultureInfo culture) => moment.HasValue ? moment.Value.ToString("t", culture) : "—";

        /// <summary>The day of the week by its number, "Day 4", and the Shabbat by its name: no names of the nations' days.</summary>
        public static string WeekdayOf(DateTime date, Func<string, string> text) => date.DayOfWeek == DayOfWeek.Saturday
            ? text("calendar_shabbat") : string.Format(text("calendar_weekday"), (int)date.DayOfWeek + 1);

        private static string WithDay(DateTime? moment, CultureInfo culture, Func<string, string> text) => moment.HasValue
            ? WeekdayOf(moment.Value, text) + " " + moment.Value.ToString("t", culture) : "—";

        public static DayDetails Describe(DateTime date, HebrewPlace place, string language, ICollection<string> hidden, Func<string, string> text)
        {
            date = date.Date;
            bool israel = IsLandOfIsrael(place);
            CultureInfo culture = CultureOf(language);
            var events = JewishCalendar.Days(date, israel).Select(day => EventName(day, language, text)).ToArray();

            // The portion of the Shabbat of the week; on the Shabbat itself it may be a festival's reading instead.
            DateTime shabbat = date;
            while (shabbat.DayOfWeek != DayOfWeek.Saturday) shabbat = shabbat.AddDays(1);
            TorahReading reading = TorahReadings.ForShabbat(shabbat, israel);
            string portion = reading != null ? string.Format(text("calendar_portion"), reading.Name(language))
                : date.DayOfWeek == DayOfWeek.Saturday && shabbat.Year > 1600 && shabbat.Year < 2200 ? text("calendar_festival_reading") : null;

            Molad molad = null;
            try { molad = Molads.Next(date); }
            catch (ArgumentOutOfRangeException) { }
            string moladText = molad == null ? null : string.Format(text("calendar_molad"), molad.MonthName(language),
                WeekdayOf(molad.MeanTime, text), molad.MeanTime.ToString("t", culture), molad.Parts);

            string placeText = null, rest = null;
            var times = new List<TimeLine>();
            if (place != null)
            {
                string zone = string.IsNullOrEmpty(place.TimeZoneId) ? null : ZoneClock.Describe(place.TimeZoneId, DateTime.UtcNow);
                // The festivals, Rosh Chodesh and portions go by the Land of Israel or the diaspora, by the place's country.
                placeText = string.Format(text("calendar_place"), (zone == null ? place.Display : place.Display + " · " + zone) + " · " + text(israel ? "calendar_israel" : "calendar_diaspora"));
                var day = Zmanim.Of(date, place.Latitude, place.Longitude);
                foreach (Zman zman in new[]
                {
                    Zman.Alot, Zman.Misheyakir, Zman.Sunrise, Zman.ShemaGra, Zman.TefillaGra, Zman.Chatzot, Zman.MinchaGedola, Zman.MinchaKetana,
                    Zman.PlagHaMincha, Zman.Sunset, Zman.Tzeit,
                })
                    times.Add(new TimeLine(Zmanim.NameOf(zman, language), Short(OnClock(day[zman], place), culture)));
                // Hanukkah: on a Friday before the Shabbat candles, otherwise at nightfall (after the havdalah).
                RestEvent evening = Zmanim.EveningOf(date, israel);
                int candles = ChanukahTonight(date);
                TimeLine chanukah = candles == 0 ? null : new TimeLine(string.Format(text("calendar_chanukah_candles"), candles),
                    Short(OnClock(day[evening == RestEvent.CandleLighting ? Zman.CandleLighting : Zman.Tzeit], place), culture), Chanukah: true);
                if (chanukah != null && evening == RestEvent.CandleLighting) times.Add(chanukah);
                switch (evening)
                {
                    case RestEvent.CandleLighting:
                        times.Add(new TimeLine(Zmanim.NameOf(Zman.CandleLighting, language), Short(OnClock(day[Zman.CandleLighting], place), culture), true));
                        break;
                    case RestEvent.CandleLightingAfterNightfall:
                        times.Add(new TimeLine(text("calendar_candles_after_dark"), Short(OnClock(day[Zman.Tzeit], place), culture), true));
                        break;
                    case RestEvent.Havdalah:
                        times.Add(new TimeLine(text("calendar_havdalah"), Short(OnClock(day[Zman.Tzeit], place), culture), true));
                        break;
                }
                if (chanukah != null && evening != RestEvent.CandleLighting) times.Add(chanukah);
                // Midnight last: after the evening's candles or havdalah.
                times.Add(new TimeLine(Zmanim.NameOf(Zman.ChatzotHaLayla, language), Short(OnClock(day[Zman.ChatzotHaLayla], place), culture)));

                // A day of rest begins at the candle lighting before it and ends at nightfall of the last day of a run of them.
                if (Zmanim.IsRestDay(date, israel))
                {
                    DateTime first = date, last = date;
                    while (Zmanim.IsRestDay(first.AddDays(-1), israel)) first = first.AddDays(-1);
                    while (Zmanim.IsRestDay(last.AddDays(1), israel)) last = last.AddDays(1);
                    var before = Zmanim.Of(first.AddDays(-1), place.Latitude, place.Longitude);
                    var after = Zmanim.Of(last, place.Latitude, place.Longitude);
                    // One line; the days only when the rest lasts more than one day.
                    DateTime? lit = OnClock(before[Zman.CandleLighting], place), dark = OnClock(after[Zman.Tzeit], place);
                    rest = first == last ? string.Format(text("calendar_rest_span"), Short(lit, culture), Short(dark, culture))
                        : string.Format(text("calendar_rest_span"), WithDay(lit, culture, text), WithDay(dark, culture, text));
                }
            }

            return new DayDetails(date.ToString("d MMMM yyyy", culture), HebrewClockFormatter.FormatDate(date, language), portion, events,
                Yahrzeits.On(date, hidden).Select(person => person.Name(language)).ToArray(), moladText, placeText, times, rest, ChanukahTonight(date));
        }

        /// <summary>
        /// The two months of the Hebrew calendar a Gregorian month spans, with the year: "Cheshvan – Kislev 5787", or with both
        /// years when the month spans the turn of the year ("Elul 5786 – Tishrei 5787").
        /// </summary>
        public static string HebrewMonths(DateTime month, string language)
        {
            var calendar = new HebrewCalendar();
            DateTime first = new DateTime(month.Year, month.Month, 1), last = first.AddMonths(1).AddDays(-1);
            string Name(DateTime day) => HebrewClockFormatter.MonthName(JewishCalendar.MonthNumber(day), calendar.IsLeapYear(calendar.GetYear(day)), language);
            int yearFirst = calendar.GetYear(first), yearLast = calendar.GetYear(last);
            string a = Name(first), b = Name(last);
            if (yearFirst != yearLast) return $"{a} {yearFirst} – {b} {yearLast}";
            return a == b ? $"{a} {yearFirst}" : $"{a} – {b} {yearFirst}";
        }
    }
}
