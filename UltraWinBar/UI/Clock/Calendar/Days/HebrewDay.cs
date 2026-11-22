using System;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// Which Hebrew day it is: Day is the date (a civil date) whose Hebrew date is the current one, and Civil, from sunset
    /// until midnight, the civil date that is still on while the Hebrew day has already turned to the next one.
    /// </summary>
    public sealed record HebrewDayNow(DateTime Day, DateTime? Civil)
    {
        public bool AfterSunset => Civil.HasValue;
    }

    /// <summary>
    /// The Hebrew day of the moment. With no place chosen it changes at midnight of the computer's clock. With a place the
    /// clock is that of the place (its time zone, or the computer's when none was chosen) and the Hebrew day begins at the
    /// sunset of the place: from sunset until midnight it is the next day, and the clock still shows the civil date.
    /// </summary>
    public static class HebrewDayClock
    {
        public static HebrewDayNow Now(DateTime utcNow, HebrewPlace place)
        {
            DateTime utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            DateTime clock = (place == null ? null : ZoneClock.Now(place.TimeZoneId, utc)) ?? TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
            DateTime date = clock.Date;
            if (place == null) return new HebrewDayNow(date, null);
            DateTime? sunset = Sun.At(date, place.Latitude, place.Longitude, Sun.SunriseZenith, false);
            return sunset.HasValue && utc >= sunset.Value ? new HebrewDayNow(date.AddDays(1), date) : new HebrewDayNow(date, null);
        }
    }
}
