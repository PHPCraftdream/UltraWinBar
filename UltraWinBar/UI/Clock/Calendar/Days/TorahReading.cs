using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>The weekly Torah portion of a Shabbat: one parasha, or two read together ("Vayakhel-Pekudei").</summary>
    public sealed record TorahReading(int First, int Second)
    {
        public bool IsDouble => Second >= 0;

        /// <summary>The name in the language of the settings ("עברית", "русский"; anything else is English).</summary>
        public string Name(string language)
        {
            IReadOnlyList<string> names = TorahReadings.NamesIn(language);
            return IsDouble ? names[First] + "-" + names[Second] : names[First];
        }
    }

    /// <summary>
    /// Which Torah portion is read on which Shabbat. The portions are read one after the other from the Shabbat after
    /// Simchat Torah to the next one; which of them are read together, and which Shabbatot are taken by a festival,
    /// follows from the kind of the year: leap or not, the weekday of Rosh Hashana, and the length of the year (the
    /// 14 possible kinds), and from whether the place is in Israel, where the festivals last a day less. The schedules
    /// are public facts of the Jewish calendar, written down here as sequences of portion numbers ("21-22" for two
    /// read together, "_" for a Shabbat that has a festival's own reading); the tests check them against the
    /// calendar's fixed points, which do not depend on them: Bamidbar the Shabbat before Shavuot, Devarim the Shabbat
    /// before Tisha B'Av, Nitzavim the Shabbat before Rosh Hashana, every portion once and in order.
    /// </summary>
    public static class TorahReadings
    {
        private static readonly HebrewCalendar Calendar = new HebrewCalendar();

        private static readonly string[] English =
        {
            "Bereshit", "Noach", "Lech-Lecha", "Vayera", "Chayei Sarah", "Toldot", "Vayetze", "Vayishlach",
            "Vayeshev", "Miketz", "Vayigash", "Vayechi", "Shemot", "Vaera", "Bo", "Beshalach", "Yitro", "Mishpatim",
            "Terumah", "Tetzaveh", "Ki Tisa", "Vayakhel", "Pekudei", "Vayikra", "Tzav", "Shemini", "Tazria",
            "Metzora", "Acharei Mot", "Kedoshim", "Emor", "Behar", "Bechukotai", "Bamidbar", "Nasso", "Behaalotecha",
            "Shlach", "Korach", "Chukat", "Balak", "Pinchas", "Matot", "Masei", "Devarim", "Vaetchanan", "Eikev",
            "Re'eh", "Shoftim", "Ki Teitzei", "Ki Tavo", "Nitzavim", "Vayeilech", "Haazinu", "Vezot Haberakhah",
        };

        private static readonly string[] Russian =
        {
            "Берешит", "Ноах", "Лех-Леха", "Ваера", "Хаей Сара", "Толдот", "Ваеце", "Ваишлах", "Ваешев", "Микец",
            "Ваигаш", "Ваехи", "Шмот", "Ваэра", "Бо", "Бешалах", "Итро", "Мишпатим", "Трума", "Тецаве", "Ки тиса",
            "Ваякхель", "Пекудей", "Ваикра", "Цав", "Шмини", "Тазриа", "Мецора", "Ахарей мот", "Кдошим", "Эмор",
            "Бегар", "Бехукотай", "Бамидбар", "Насо", "Беаалотха", "Шлах", "Корах", "Хукат", "Балак", "Пинхас",
            "Матот", "Масей", "Дварим", "Ваэтханан", "Экев", "Рэ'э", "Шофтим", "Ки тейце", "Ки таво", "Ницавим",
            "Ваелех", "Гаазину", "Вэзот абраха",
        };

        private static readonly string[] Hebrew =
        {
            "בראשית", "נח", "לך לך", "וירא", "חיי שרה", "תולדות", "ויצא", "וישלח", "וישב", "מקץ", "ויגש", "ויחי",
            "שמות", "וארא", "בא", "בשלח", "יתרו", "משפטים", "תרומה", "תצוה", "כי תשא", "ויקהל", "פקודי", "ויקרא",
            "צו", "שמיני", "תזריע", "מצורע", "אחרי מות", "קדושים", "אמור", "בהר", "בחוקותי", "במדבר", "נשא",
            "בהעלותך", "שלח", "קרח", "חקת", "בלק", "פינחס", "מטות", "מסעי", "דברים", "ואתחנן", "עקב", "ראה",
            "שופטים", "כי תצא", "כי תבא", "נצבים", "וילך", "האזינו", "וזאת הברכה",
        };

        // The schedules, one token per Shabbat from the Shabbat after Simchat Torah: a portion number, "a-b" for two
        // read together, "_" for a Shabbat with a festival reading. The last portion, 52, is Haazinu; 53, Vezot
        // Haberakhah, is read on Simchat Torah, not on a Shabbat.
        private static readonly string[][] Schedules = new[]
        {
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 _ 34 35 36 37 38-39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 _ 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 _ 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50-51 52 _ _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 _ 34 35 36 37 38-39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _",
            "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _",
        }.Select(sequence => sequence.Split(' ')).ToArray();

        // (leap year, weekday of Rosh Hashana, days in the year) => (schedule in the diaspora, schedule in Israel).
        private static readonly Dictionary<(bool, DayOfWeek, int), (int Diaspora, int Israel)> Types =
            new Dictionary<(bool, DayOfWeek, int), (int, int)>
        {
            [(false, DayOfWeek.Monday, 353)] = (1, 1),
            [(false, DayOfWeek.Monday, 355)] = (5, 4),
            [(false, DayOfWeek.Tuesday, 354)] = (5, 4),
            [(false, DayOfWeek.Thursday, 354)] = (6, 3),
            [(false, DayOfWeek.Thursday, 355)] = (2, 2),
            [(false, DayOfWeek.Saturday, 353)] = (0, 0),
            [(false, DayOfWeek.Saturday, 355)] = (1, 1),
            [(true, DayOfWeek.Monday, 383)] = (12, 11),
            [(true, DayOfWeek.Monday, 385)] = (13, 10),
            [(true, DayOfWeek.Tuesday, 384)] = (13, 10),
            [(true, DayOfWeek.Thursday, 383)] = (8, 8),
            [(true, DayOfWeek.Thursday, 385)] = (9, 9),
            [(true, DayOfWeek.Saturday, 383)] = (7, 7),
            [(true, DayOfWeek.Saturday, 385)] = (12, 11),
        };

        public static IReadOnlyList<string> NamesIn(string language) =>
            language == "עברית" ? Hebrew : language == "русский" ? Russian : English;

        /// <summary>
        /// The portion read on the Shabbat of the date; null when the date is not a Shabbat, is outside the Hebrew
        /// calendar's range, or the Shabbat has a festival's own reading (Pesach, Sukkot, Rosh Hashana and the like).
        /// </summary>
        public static TorahReading ForShabbat(DateTime date, bool israel)
        {
            date = date.Date;
            if (date.DayOfWeek != DayOfWeek.Saturday || date < Calendar.MinSupportedDateTime.AddYears(2) || date > Calendar.MaxSupportedDateTime.AddYears(-2))
                return null;
            int year = Calendar.GetYear(date);
            if (date < FirstShabbat(year, israel)) year--;
            int week = (date - FirstShabbat(year, israel)).Days / 7;
            string[] schedule = Schedules[ScheduleOf(year, israel)];
            return week < schedule.Length ? Parse(schedule[week]) : null;
        }

        /// <summary>The portions of the year from the Shabbat after Simchat Torah on: the Shabbat and what is read.</summary>
        public static IEnumerable<(DateTime Shabbat, TorahReading Reading)> Year(int hebrewYear, bool israel)
        {
            DateTime shabbat = FirstShabbat(hebrewYear, israel);
            foreach (string token in Schedules[ScheduleOf(hebrewYear, israel)])
            {
                yield return (shabbat, Parse(token));
                shabbat = shabbat.AddDays(7);
            }
        }

        /// <summary>The first Shabbat after Simchat Torah (23 Tishrei; 22 in Israel), when Bereshit is read.</summary>
        public static DateTime FirstShabbat(int hebrewYear, bool israel)
        {
            DateTime day = Calendar.ToDateTime(hebrewYear, 1, israel ? 22 : 23, 0, 0, 0, 0).AddDays(1);
            while (day.DayOfWeek != DayOfWeek.Saturday) day = day.AddDays(1);
            return day;
        }

        private static int ScheduleOf(int hebrewYear, bool israel)
        {
            var kind = (Calendar.IsLeapYear(hebrewYear), Calendar.ToDateTime(hebrewYear, 1, 1, 0, 0, 0, 0).DayOfWeek, Calendar.GetDaysInYear(hebrewYear));
            return israel ? Types[kind].Israel : Types[kind].Diaspora;
        }

        private static TorahReading Parse(string token)
        {
            if (token == "_") return null;
            string[] parts = token.Split('-');
            return new TorahReading(int.Parse(parts[0], CultureInfo.InvariantCulture),
                parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : -1);
        }
    }
}
