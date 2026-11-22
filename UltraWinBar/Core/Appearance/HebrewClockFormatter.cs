using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace UltraWinBar.Utilities
{
    public static class HebrewClockFormatter
    {
        private sealed class CalendarLocale
        {
            public string Locale { get; set; }
            public string Language { get; set; }
            public Dictionary<string, string> Months { get; set; }
        }
        private static readonly CalendarLocale[] locales = LoadLocales();
        private static CalendarLocale[] LoadLocales()
        {
            using var data = typeof(HebrewClockFormatter).Assembly.GetManifestResourceStream("UltraWinBar.Resources.HebrewMonths.json");
            return JsonSerializer.Deserialize<CalendarLocale[]>(data);
        }
        public static IReadOnlyList<string> SupportedLanguages => locales.Select(locale => locale.Language).ToArray();

        public static string DayName(DayOfWeek day) => day == DayOfWeek.Saturday
            ? "Shabbat" : "Day-" + ((int)day + 1).ToString(CultureInfo.InvariantCulture);

        private static CalendarLocale Resolve(string language, CultureInfo systemCulture)
        {
            var locale = locales.FirstOrDefault(item => item.Language == language || item.Locale == language);
            if (locale == null)
            {
                var culture = systemCulture ?? CultureInfo.CurrentUICulture;
                while (locale == null && culture != CultureInfo.InvariantCulture)
                {
                    locale = locales.FirstOrDefault(item => item.Locale.Equals(culture.Name, StringComparison.OrdinalIgnoreCase));
                    culture = culture.Parent;
                }
            }
            return locale ?? locales.First(item => item.Locale == "en");
        }

        /// <summary>
        /// The name of a month. The number counts from Tishrei = 1 and always has room for both Adars: 6 is Adar I
        /// (leap years only), 7 is Adar (Adar II in a leap year), Nisan is 8, Elul 13.
        /// </summary>
        public static string MonthName(int month, bool leap, string language = "English", CultureInfo systemCulture = null)
        {
            string monthKey = leap && month == 7 ? "7-yeartype-leap" : month.ToString(CultureInfo.InvariantCulture);
            return Resolve(language, systemCulture).Months[monthKey];
        }

        /// <summary>
        /// The weekday label of the Hebrew day that begins at sunset: the new day and, in brackets, the weekday of the
        /// civil day that is still on until midnight ("Day-2[1]", "Shabbat[6]", "Day-1[Shabbat]").
        /// </summary>
        public static string DayName(DayOfWeek day, DayOfWeek? civilDay)
        {
            if (civilDay == null) return DayName(day);
            string civil = civilDay.Value == DayOfWeek.Saturday ? "Shabbat" : ((int)civilDay.Value + 1).ToString(CultureInfo.InvariantCulture);
            return DayName(day) + "[" + civil + "]";
        }

        /// <summary>
        /// The Hebrew date as "01 Kislev 5777". For a Hebrew day that began at sunset the number of the day is followed by
        /// the number the civil day that is still on has, in brackets: "02[01] Kislev 5777" until midnight.
        /// </summary>
        public static string FormatDate(DateTime date, string language = "English", CultureInfo systemCulture = null, DateTime? civilDay = null)
        {
            var calendar = new HebrewCalendar();
            if (date < calendar.MinSupportedDateTime || date > calendar.MaxSupportedDateTime) return string.Empty;
            int year = calendar.GetYear(date);
            int month = calendar.GetMonth(date);
            bool leap = calendar.IsLeapYear(year);
            if (!leap && month >= 6) month++;
            string day = calendar.GetDayOfMonth(date).ToString("00", CultureInfo.InvariantCulture);
            if (civilDay.HasValue) day += "[" + calendar.GetDayOfMonth(civilDay.Value).ToString("00", CultureInfo.InvariantCulture) + "]";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", day, MonthName(month, leap, language, systemCulture), year);
        }
    }
}
