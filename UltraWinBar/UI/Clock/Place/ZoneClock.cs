using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// Time zones by their IANA identifier ("Asia/Jerusalem"), with the rules the computer has. Windows brings the rules
    /// up to date with its updates, and a zone applies its changes of the offset (summer time, a country moving its
    /// clocks) on the date it is asked about, so a time is always the one the law of the zone gives for that moment.
    /// Nothing is kept for long: <see cref="Refresh"/> forgets what was read, and is called when the computer's time or
    /// zone changes and now and then.
    /// </summary>
    public static class ZoneClock
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, TimeZoneInfo> Found = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);

        /// <summary>The zone, or null when the id is empty or this computer does not know it.</summary>
        public static TimeZoneInfo Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (Gate)
            {
                if (Found.TryGetValue(id, out TimeZoneInfo zone)) return zone;
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { zone = null; }
                catch (InvalidTimeZoneException) { zone = null; }
                catch (System.Security.SecurityException) { zone = null; }
                Found[id] = zone;
                return zone;
            }
        }

        /// <summary>Forgets the zones read so far and the computer's own cache, so the rules are read again.</summary>
        public static void Refresh()
        {
            lock (Gate) Found.Clear();
            TimeZoneInfo.ClearCachedData();
        }

        private static DateTime AsUtc(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        /// <summary>The time on the clocks of the zone at the moment; null for an unknown zone.</summary>
        public static DateTime? Now(string id, DateTime utcNow)
        {
            TimeZoneInfo zone = Find(id);
            return zone == null ? (DateTime?)null : TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcNow), zone);
        }

        /// <summary>How far the clocks of the zone are from UTC at the moment; null for an unknown zone.</summary>
        public static TimeSpan? Offset(string id, DateTime utcNow) => Find(id)?.GetUtcOffset(AsUtc(utcNow));

        /// <summary>"+02:00", "-03:30", "+00:00".</summary>
        public static string FormatOffset(TimeSpan offset) =>
            (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString("hh\\:mm", CultureInfo.InvariantCulture);

        /// <summary>The place a zone is named after, without the region: "Buenos Aires" for "America/Argentina/Buenos_Aires".</summary>
        public static string ShortName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string last = id.Substring(id.LastIndexOf('/') + 1);
            return last.Replace('_', ' ');
        }

        /// <summary>
        /// The computer's own zone as Windows names it, by its first place: "Amsterdam" for "(UTC+01:00) Amsterdam, Berlin,
        /// Bern, ..." (in the language of Windows).
        /// </summary>
        public static string LocalName()
        {
            string display = TimeZoneInfo.Local.DisplayName ?? "";
            int close = display.StartsWith("(", StringComparison.Ordinal) ? display.IndexOf(')') : -1;
            string rest = (close >= 0 ? display.Substring(close + 1) : display).Trim();
            int comma = rest.IndexOf(',');
            string name = (comma > 0 ? rest.Substring(0, comma) : rest).Trim();
            return name.Length > 0 ? name : TimeZoneInfo.Local.StandardName;
        }

        /// <summary>"Asia/Jerusalem (UTC+02:00)" at the moment; the id alone for an unknown zone.</summary>
        public static string Describe(string id, DateTime utcNow)
        {
            TimeSpan? offset = Offset(id, utcNow);
            return offset.HasValue ? $"{id} (UTC{FormatOffset(offset.Value)})" : id;
        }

        /// <summary>
        /// The moment (UTC) of a time on the clocks of the zone; null for an unknown zone. A time the clocks skip at a
        /// change is read as the hour after it, a time they repeat as the first of the two.
        /// </summary>
        public static DateTime? ToUtc(string id, DateTime local)
        {
            TimeZoneInfo zone = Find(id);
            if (zone == null) return null;
            DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
            if (zone.IsAmbiguousTime(unspecified))
            {
                TimeSpan first = zone.GetAmbiguousTimeOffsets(unspecified).Max();
                return DateTime.SpecifyKind(unspecified - first, DateTimeKind.Utc);
            }
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }
    }
}
