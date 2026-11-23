using System;
using System.Globalization;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The months the calendar's grid can show: a civil month (1 January to 31 January) or a Hebrew one (1 Kislev to 29 or
    /// 30 Kislev). A month is named by the date of its first day.
    /// </summary>
    public static class CalendarGrid
    {
        private static readonly HebrewCalendar Hebrew = new HebrewCalendar();

        /// <summary>The first day of the month that holds the day.</summary>
        public static DateTime MonthStart(DateTime day, bool hebrew)
        {
            day = day.Date;
            return hebrew ? day.AddDays(1 - Hebrew.GetDayOfMonth(day)) : new DateTime(day.Year, day.Month, 1);
        }

        /// <summary>How many days the month that begins on start has.</summary>
        public static int Length(DateTime start, bool hebrew) => hebrew
            ? Hebrew.GetDaysInMonth(Hebrew.GetYear(start), Hebrew.GetMonth(start))
            : DateTime.DaysInMonth(start.Year, start.Month);

        public static DateTime Next(DateTime start, bool hebrew) => hebrew ? start.AddDays(Length(start, true)) : start.AddMonths(1);

        public static DateTime Previous(DateTime start, bool hebrew) => hebrew ? MonthStart(start.AddDays(-1), true) : start.AddMonths(-1);

        public static bool InMonth(DateTime day, DateTime start, bool hebrew) => MonthStart(day, hebrew) == start.Date;

        /// <summary>
        /// The 35 days of the grid, five weeks from the Sunday of the month's first week. A month that would need a sixth week
        /// puts its last days in the first week, in the places of the days before it began (as a paper calendar does).
        /// </summary>
        public static DateTime[] Days(DateTime start, bool hebrew)
        {
            DateTime first = start.AddDays(-(int)start.DayOfWeek);
            var days = new DateTime[35];
            for (int i = 0; i < days.Length; i++)
            {
                DateTime day = first.AddDays(i), wrapped = day.AddDays(35);
                days[i] = day < start && InMonth(wrapped, start, hebrew) ? wrapped : day;
            }
            return days;
        }

        /// <summary>The day with the chosen day's number in another month, or that month's last day when it is shorter.</summary>
        public static DateTime SameDay(DateTime chosen, DateTime otherStart, bool hebrew)
        {
            int number = hebrew ? Hebrew.GetDayOfMonth(chosen) : chosen.Day;
            return otherStart.AddDays(Math.Min(number, Length(otherStart, hebrew)) - 1);
        }
    }
}
