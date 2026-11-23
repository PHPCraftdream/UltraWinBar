namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The three time zones of the clock: the main one is the computer's own, the second and the third are chosen in the
    /// settings. The calendar goes by one of the three for the date, the change of the day and the times of the day.
    /// </summary>
    public static class ClockZones
    {
        public const int Main = 0, Second = 1, Third = 2;

        /// <summary>The IANA id of a zone ("Asia/Jerusalem"); empty for one not chosen, and for the main one when it is the computer's.</summary>
        public static string IdOf(int zone) => zone == Second ? Settings.Instance.SecondTimeZone : zone == Third ? Settings.Instance.ThirdTimeZone
            : ZoneClock.Find(Settings.Instance.MainTimeZone) != null ? Settings.Instance.MainTimeZone : "";

        /// <summary>The time on the clock of the main zone: the one chosen, or the computer's.</summary>
        public static System.DateTime MainTime(System.DateTime utcNow) =>
            ZoneClock.Now(IdOf(Main), utcNow) ?? System.TimeZoneInfo.ConvertTimeFromUtc(System.DateTime.SpecifyKind(utcNow, System.DateTimeKind.Utc), System.TimeZoneInfo.Local);

        /// <summary>The main zone always is; the others when one is chosen and this computer knows it.</summary>
        public static bool IsSet(int zone) => zone == Main || ZoneClock.Find(IdOf(zone)) != null;

        /// <summary>Whether the clock names the zone next to its time.</summary>
        public static bool ShowsName(int zone) => zone == Main ? Settings.Instance.ShowMainZoneName
            : zone == Second ? Settings.Instance.ShowSecondZoneName : Settings.Instance.ShowThirdZoneName;

        /// <summary>The name shown for the zone: the place it is named after ("Jerusalem"); the computer's own zone as Windows names it.</summary>
        public static string NameOf(int zone) => zone == Main && IdOf(Main) == "" ? ZoneClock.LocalName() : ZoneClock.ShortName(IdOf(zone));

        /// <summary>The zone the calendar goes by: its choice, or the main one when the chosen zone is not set.</summary>
        public static int CalendarZone() => IsSet(Settings.Instance.CalendarZone) ? Settings.Instance.CalendarZone : Main;

        /// <summary>The place of the zone: where its times of the day are those of. Null when none is chosen.</summary>
        public static HebrewPlace PlaceOf(int zone) =>
            zone == Second ? Settings.Instance.SecondPlace : zone == Third ? Settings.Instance.ThirdPlace : Settings.Instance.HebrewPlace;

        public static void SetPlace(int zone, HebrewPlace place)
        {
            if (zone == Second) Settings.Instance.SecondPlace = place;
            else if (zone == Third) Settings.Instance.ThirdPlace = place;
            else Settings.Instance.HebrewPlace = place;
        }

        /// <summary>
        /// The place of the calendar: the place of the zone the calendar goes by, with the clock of that zone (the computer's
        /// clock for the main one): its date, its day that turns at its own sunset, its times of the day. Null when the zone
        /// has no place.
        /// </summary>
        public static HebrewPlace CalendarPlace()
        {
            int zone = CalendarZone();
            return PlaceOf(zone) is HebrewPlace place ? place with { TimeZoneId = IdOf(zone) } : null;
        }
    }
}
