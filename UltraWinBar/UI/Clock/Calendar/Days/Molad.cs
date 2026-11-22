using System;
using System.Globalization;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The molad, the moment of the new moon the calendar counts from, of a month. MeanTime is the Jerusalem mean solar
    /// time (not a civil clock, and not shifted for summer time) as it is announced, to the minute; Parts are the
    /// eighteenths of a minute on top ("12 minutes and 5 chalakim").
    /// </summary>
    public sealed record Molad(int Year, int Month, bool LeapYear, DateTime MeanTime, int Parts)
    {
        public string MonthName(string language) => HebrewClockFormatter.MonthName(Month, LeapYear, language);
    }

    /// <summary>
    /// The molad of any month of the Hebrew calendar, by the arithmetic the calendar is made of: a month is 29 days, 12
    /// hours and 793 chalakim (parts of 1/1080 hour) long, the molad of the first Tishrei (year 1) was on day 2 (Monday,
    /// counting the day from the evening) at 5 hours and 204 chalakim, and the months pass at a constant length. The
    /// day of the molad of Tishrei is Rosh Hashana, or one or two days before it, which pins the dates to the calendar
    /// of the .NET runtime.
    /// </summary>
    public static class Molads
    {
        private static readonly HebrewCalendar Calendar = new HebrewCalendar();

        private const long PartsPerHour = 1080, PartsPerDay = 24 * PartsPerHour, PartsPerMonth = 29 * PartsPerDay + 12 * PartsPerHour + 793;
        private const long FirstMolad = PartsPerDay + 5 * PartsPerHour + 204;

        /// <summary>The molad of a month of a Hebrew year; the month is numbered from Tishrei = 1, with 6 for Adar I.</summary>
        public static Molad Of(int hebrewYear, int month)
        {
            bool leap = Calendar.IsLeapYear(hebrewYear);
            if (month < 1 || month > 13 || month == 6 && !leap) throw new ArgumentOutOfRangeException(nameof(month));
            long tishrei = FirstMolad + (235L * hebrewYear - 234) / 19 * PartsPerMonth;
            long moment = tishrei + (!leap && month >= 7 ? month - 2 : month - 1) * PartsPerMonth;

            // The day the molad of Tishrei counts for; it is never later than Rosh Hashana, and no more than two days before it.
            DateTime roshHashana = Calendar.ToDateTime(hebrewYear, 1, 1, 0, 0, 0, 0);
            int before = ((int)roshHashana.DayOfWeek - (int)(tishrei / PartsPerDay % 7) + 7) % 7;
            DateTime day = roshHashana.AddDays(-before + (moment / PartsPerDay - tishrei / PartsPerDay));

            // A day begins at 18:00 of the evening before.
            long rest = moment % PartsPerDay;
            long parts = rest % PartsPerHour;
            DateTime time = day.AddDays(-1).AddHours(18 + rest / PartsPerHour).AddMinutes(parts / 18);
            return new Molad(hebrewYear, month, leap, time, (int)(parts % 18));
        }

        /// <summary>The molad of the month that follows the month of the date (the one blessed on the Shabbat before it).</summary>
        public static Molad Next(DateTime date)
        {
            date = date.Date;
            DateTime first = date.AddDays(Calendar.GetDaysInMonth(Calendar.GetYear(date), Calendar.GetMonth(date)) - Calendar.GetDayOfMonth(date) + 1);
            return Of(Calendar.GetYear(first), JewishCalendar.MonthNumber(first));
        }
    }
}
