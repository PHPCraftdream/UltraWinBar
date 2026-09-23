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

        public static string FormatDate(DateTime date, string language = "English", CultureInfo systemCulture = null)
        {
            var calendar = new HebrewCalendar();
            if (date < calendar.MinSupportedDateTime || date > calendar.MaxSupportedDateTime) return string.Empty;
            int year = calendar.GetYear(date);
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
            locale ??= locales.First(item => item.Locale == "en");
            int month = calendar.GetMonth(date);
            bool leap = calendar.IsLeapYear(year);
            if (!leap && month >= 6) month++;
            string monthKey = leap && month == 7 ? "7-yeartype-leap" : month.ToString(CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, "{0:00} {1} {2}",
                calendar.GetDayOfMonth(date), locale.Months[monthKey], year);
        }
    }
}
