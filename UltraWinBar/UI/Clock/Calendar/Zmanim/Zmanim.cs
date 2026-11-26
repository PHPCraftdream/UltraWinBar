using System;
using System.Collections.Generic;

namespace UltraWinBar.Utilities
{
    public enum Zman
    {
        Alot, Misheyakir, Sunrise, ShemaMagenAvraham, ShemaGra, TefillaMagenAvraham, TefillaGra, Chatzot, MinchaGedola,
        MinchaKetana, PlagHaMincha, CandleLighting, Sunset, Tzeit, RabbeinuTam, ChatzotHaLayla,
    }

    /// <summary>What happens at the turn of a day to a day of rest: candles are lit, or the rest ends (havdalah).</summary>
    public enum RestEvent { None, CandleLighting, CandleLightingAfterNightfall, Havdalah }

    /// <summary>
    /// The halachic times of a day at a place ("zmanim"), in UTC, computed from the sun (see <see cref="Sun"/>); a time is
    /// null when the sun does not reach the angle that day (the far north in summer). The opinions are fixed, the commonly
    /// kept ones: the hours of the day are the twelfths of the time from sunrise to sunset (the Gra), or from 72 minutes
    /// before sunrise to 72 minutes after sunset (the Magen Avraham); dawn is the sun 16.1° below the horizon, the earliest
    /// time for tallit and tefillin 11.5°, nightfall (tzeit) 8.5°; Rabbeinu Tam's nightfall is 72 minutes after sunset;
    /// candles are lit 18 minutes before sunset (40 in Jerusalem is a custom of the place, passed as the argument); midnight
    /// (chatzot halayla) is halfway from sunset to the next sunrise.
    /// </summary>
    public static class Zmanim
    {
        private static readonly Dictionary<Zman, string[]> Names = new Dictionary<Zman, string[]>
        {
            [Zman.Alot] = new[] { "Dawn (Alot HaShachar)", "Рассвет (алот а-шахар)", "עלות השחר" },
            [Zman.Misheyakir] = new[] { "Earliest tallit and tefillin", "Время для талита и тфилин", "משיכיר" },
            [Zman.Sunrise] = new[] { "Sunrise (Netz)", "Восход (нец)", "הנץ החמה" },
            [Zman.ShemaMagenAvraham] = new[] { "Latest Shema (Magen Avraham)", "Конец времени Шма (Маген Авраам)", "סוף זמן קריאת שמע (מגן אברהם)" },
            [Zman.ShemaGra] = new[] { "Latest Shema (Gra)", "Конец времени Шма (Гра)", "סוף זמן קריאת שמע (גר״א)" },
            [Zman.TefillaMagenAvraham] = new[] { "Latest Shacharit (Magen Avraham)", "Конец времени молитвы (Маген Авраам)", "סוף זמן תפילה (מגן אברהם)" },
            [Zman.TefillaGra] = new[] { "Latest Shacharit (Gra)", "Конец времени молитвы (Гра)", "סוף זמן תפילה (גר״א)" },
            [Zman.Chatzot] = new[] { "Midday (Chatzot)", "Полдень (хацот)", "חצות היום" },
            [Zman.MinchaGedola] = new[] { "Earliest Mincha (Mincha Gedola)", "Минха гдола", "מנחה גדולה" },
            [Zman.MinchaKetana] = new[] { "Mincha Ketana", "Минха кцана", "מנחה קטנה" },
            [Zman.PlagHaMincha] = new[] { "Plag HaMincha", "Плаг а-минха", "פלג המנחה" },
            [Zman.CandleLighting] = new[] { "Candle lighting", "Зажигание свечей", "הדלקת נרות" },
            [Zman.Sunset] = new[] { "Sunset (Shkiah)", "Закат (шкия)", "שקיעה" },
            [Zman.Tzeit] = new[] { "Nightfall (Tzeit HaKochavim)", "Выход звёзд (цет а-кохавим)", "צאת הכוכבים" },
            [Zman.RabbeinuTam] = new[] { "Nightfall (Rabbeinu Tam)", "Выход звёзд (раввейну Там)", "צאת רבינו תם" },
            [Zman.ChatzotHaLayla] = new[] { "Midnight (Chatzot HaLayla)", "Полночь (хацот а-лайла)", "חצות הלילה" },
        };

        public static string NameOf(Zman zman, string language) => Names[zman][language == "עברית" ? 2 : language == "русский" ? 1 : 0];

        private static DateTime? Plus(DateTime? time, double minutes) => time?.AddMinutes(minutes);

        // The moment of a given hour of a day that runs from start to end divided into twelve hours.
        private static DateTime? Hour(DateTime? start, DateTime? end, double hours) =>
            start.HasValue && end.HasValue ? start.Value + TimeSpan.FromTicks((long)((end.Value - start.Value).Ticks / 12.0 * hours)) : (DateTime?)null;

        /// <summary>The times of the solar day of the date at the place (latitude and longitude in degrees, east positive).</summary>
        public static IReadOnlyDictionary<Zman, DateTime?> Of(DateTime date, double latitude, double longitude, int candleLightingMinutes = 18)
        {
            DateTime? sunrise = Sun.At(date, latitude, longitude, Sun.SunriseZenith, true);
            DateTime? sunset = Sun.At(date, latitude, longitude, Sun.SunriseZenith, false);
            DateTime? dayStart = Plus(sunrise, -72), dayEnd = Plus(sunset, 72);
            DateTime? nextSunrise = Sun.At(date.Date.AddDays(1), latitude, longitude, Sun.SunriseZenith, true);
            return new Dictionary<Zman, DateTime?>
            {
                [Zman.Alot] = Sun.At(date, latitude, longitude, 106.1, true),
                [Zman.Misheyakir] = Sun.At(date, latitude, longitude, 101.5, true),
                [Zman.Sunrise] = sunrise,
                [Zman.ShemaMagenAvraham] = Hour(dayStart, dayEnd, 3),
                [Zman.ShemaGra] = Hour(sunrise, sunset, 3),
                [Zman.TefillaMagenAvraham] = Hour(dayStart, dayEnd, 4),
                [Zman.TefillaGra] = Hour(sunrise, sunset, 4),
                [Zman.Chatzot] = Hour(sunrise, sunset, 6),
                [Zman.MinchaGedola] = Hour(sunrise, sunset, 6.5),
                [Zman.MinchaKetana] = Hour(sunrise, sunset, 9.5),
                [Zman.PlagHaMincha] = Hour(sunrise, sunset, 10.75),
                [Zman.CandleLighting] = Plus(sunset, -candleLightingMinutes),
                [Zman.Sunset] = sunset,
                [Zman.Tzeit] = Sun.At(date, latitude, longitude, 98.5, false),
                [Zman.RabbeinuTam] = Plus(sunset, 72),
                [Zman.ChatzotHaLayla] = Hour(sunset, nextSunrise, 6),
            };
        }

        /// <summary>
        /// The date whose Hebrew day it is at the moment: the Hebrew day begins at sunset, so after the sunset of the date it
        /// is already the next day. With no sunset (the polar day) the day is the date.
        /// </summary>
        public static DateTime HebrewDay(DateTime date, DateTime utcNow, double latitude, double longitude)
        {
            DateTime? sunset = Sun.At(date.Date, latitude, longitude, Sun.SunriseZenith, false);
            return sunset.HasValue && utcNow >= sunset.Value ? date.Date.AddDays(1) : date.Date;
        }

        /// <summary>A day of rest: Shabbat or a festival on which work is forbidden.</summary>
        public static bool IsRestDay(DateTime date, bool israel)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday) return true;
            foreach (var day in JewishCalendar.Days(date, israel))
                if (day.IsYomTov) return true;
            return false;
        }

        /// <summary>
        /// What the evening of the date brings: candles when a day of rest follows, lit before sunset, except after a day of
        /// rest that is a festival and before a festival that is not Shabbat (the second day of a festival), when they are
        /// lit from an existing flame after nightfall; havdalah when the date is a day of rest and the next is not.
        /// </summary>
        public static RestEvent EveningOf(DateTime date, bool israel)
        {
            bool today = IsRestDay(date, israel), tomorrow = IsRestDay(date.AddDays(1), israel);
            if (tomorrow) return !today || date.AddDays(1).DayOfWeek == DayOfWeek.Saturday ? RestEvent.CandleLighting : RestEvent.CandleLightingAfterNightfall;
            return today ? RestEvent.Havdalah : RestEvent.None;
        }
    }
}
