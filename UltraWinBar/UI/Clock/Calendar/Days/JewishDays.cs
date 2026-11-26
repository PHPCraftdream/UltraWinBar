using System;
using System.Collections.Generic;
using System.Globalization;

namespace UltraWinBar.Utilities
{
    public enum DayKind { YomTov, CholHamoed, Fast, Minor, RoshChodesh }

    /// <summary>
    /// What makes a day special in the Jewish calendar: a festival or a day of it, a fast, a minor or a modern day, Rosh
    /// Chodesh. Number is the day within the festival (Hanukkah: 1 to 8), Count the days it lasts here (Pesach: 7 in
    /// Israel, 8 elsewhere); for Rosh Chodesh Month and LeapYear name the month that begins.
    /// </summary>
    public sealed record JewishDay(string Key, DayKind Kind, int Number, int Count, int Month = 0, bool LeapYear = false)
    {
        public bool IsFirst => Number == 1;
        public bool IsLast => Number == Count;

        /// <summary>A day on which work is forbidden, as the Shabbat is: it begins at nightfall of the evening before.</summary>
        public bool IsYomTov => Kind == DayKind.YomTov;

        /// <summary>The name in the language of the settings ("עברית", "русский"; anything else is English).</summary>
        public string Name(string language)
        {
            string name = JewishCalendar.NameOf(Key, language);
            return Key == "RoshChodesh" ? name + " " + HebrewClockFormatter.MonthName(Month, LeapYear, language) : name;
        }
    }

    /// <summary>
    /// The festivals, fasts and Rosh Chodesh of the Jewish calendar by date, from the rules of the calendar itself: each
    /// falls on a fixed day of a Hebrew month, moved when it would meet the Shabbat. Israel keeps one day less of
    /// Sukkot's end, Pesach and Shavuot than the diaspora. Months are numbered from Tishrei = 1 with room for both Adars
    /// (see <see cref="HebrewClockFormatter.MonthName"/>), unlike the .NET calendar, which has no month 6 in a plain year.
    /// </summary>
    public static class JewishCalendar
    {
        private static readonly HebrewCalendar Calendar = new HebrewCalendar();

        private const int Tishrei = 1, Kislev = 3, Tevet = 4, Shevat = 5, AdarI = 6, Adar = 7, Nisan = 8, Iyar = 9, Sivan = 10, Tammuz = 11, Av = 12;

        private static readonly Dictionary<string, string[]> Names = new Dictionary<string, string[]>
        {
            ["RoshHashana"] = new[] { "Rosh Hashana", "Рош а-Шана", "ראש השנה" },
            ["FastOfGedaliah"] = new[] { "Fast of Gedaliah", "Пост Гедалии", "צום גדליה" },
            ["YomKippur"] = new[] { "Yom Kippur", "Йом-Киппур", "יום כיפור" },
            ["Sukkot"] = new[] { "Sukkot", "Суккот", "סוכות" },
            ["CholHamoedSukkot"] = new[] { "Chol HaMoed Sukkot", "Холь а-моэд Суккот", "חול המועד סוכות" },
            ["HoshanaRabba"] = new[] { "Hoshana Rabba", "Гошана Раба", "הושענא רבה" },
            ["SheminiAtzeret"] = new[] { "Shemini Atzeret", "Шмини Ацерет", "שמיני עצרת" },
            ["SimchatTorah"] = new[] { "Simchat Torah", "Симхат Тора", "שמחת תורה" },
            ["Hanukkah"] = new[] { "Hanukkah", "Ханука", "חנוכה" },
            ["FastOfTevet"] = new[] { "Fast of 10 Tevet", "Пост 10 тевета", "עשרה בטבת" },
            ["TuBishvat"] = new[] { "Tu BiShvat", "Ту би-Шват", "ט״ו בשבט" },
            ["PurimKatan"] = new[] { "Purim Katan", "Пурим катан", "פורים קטן" },
            ["FastOfEsther"] = new[] { "Fast of Esther", "Пост Эстер", "תענית אסתר" },
            ["Purim"] = new[] { "Purim", "Пурим", "פורים" },
            ["ShushanPurim"] = new[] { "Shushan Purim", "Шушан Пурим", "שושן פורים" },
            ["Pesach"] = new[] { "Pesach", "Песах", "פסח" },
            ["CholHamoedPesach"] = new[] { "Chol HaMoed Pesach", "Холь а-моэд Песах", "חול המועד פסח" },
            ["YomHashoah"] = new[] { "Yom HaShoah", "Йом а-Шоа", "יום השואה" },
            ["YomHazikaron"] = new[] { "Yom HaZikaron", "Йом а-Зикарон", "יום הזיכרון" },
            ["YomHaatzmaut"] = new[] { "Yom HaAtzmaut", "Йом а-Ацмаут", "יום העצמאות" },
            ["PesachSheni"] = new[] { "Pesach Sheni", "Песах шени", "פסח שני" },
            ["LagBaomer"] = new[] { "Lag BaOmer", "Лаг ба-Омер", "ל״ג בעומר" },
            ["YomYerushalayim"] = new[] { "Yom Yerushalayim", "Йом Йерушалаим", "יום ירושלים" },
            ["Shavuot"] = new[] { "Shavuot", "Шавуот", "שבועות" },
            ["FastOfTammuz"] = new[] { "Fast of 17 Tammuz", "Пост 17 таммуза", "שבעה עשר בתמוז" },
            ["TishaBeav"] = new[] { "Tisha B'Av", "Тиша бе-Ав", "תשעה באב" },
            ["TuBeav"] = new[] { "Tu B'Av", "Ту бе-Ав", "ט״ו באב" },
            ["RoshChodesh"] = new[] { "Rosh Chodesh", "Рош ходеш", "ראש חודש" },
        };

        public static IReadOnlyCollection<string> Keys => Names.Keys;

        public static string NameOf(string key, string language) =>
            Names[key][language == "עברית" ? 2 : language == "русский" ? 1 : 0];

        /// <summary>The number of the month in the numbering above (Tishrei = 1, Adar I = 6, Adar = 7, Nisan = 8, Elul = 13).</summary>
        public static int MonthNumber(DateTime date)
        {
            int month = Calendar.GetMonth(date);
            return !Calendar.IsLeapYear(Calendar.GetYear(date)) && month >= 6 ? month + 1 : month;
        }

        /// <summary>
        /// The day of the counting of the Omer (1 on 16 Nisan to 49 on 5 Sivan); 0 outside it or outside the Hebrew calendar.
        /// The count of a day is said on the evening that begins it.
        /// </summary>
        public static int OmerDay(DateTime date)
        {
            date = date.Date;
            if (date < Calendar.MinSupportedDateTime || date > Calendar.MaxSupportedDateTime) return 0;
            int month = MonthNumber(date), day = Calendar.GetDayOfMonth(date);
            return month == Nisan && day >= 16 ? day - 15 : month == Iyar ? 15 + day : month == Sivan && day <= 5 ? 44 + day : 0;
        }

        // The seven sefirot of the weeks and days of the Omer, in English, Russian and Hebrew.
        private static readonly string[][] Sefirot =
        {
            new[] { "Chesed", "Gevurah", "Tiferet", "Netzach", "Hod", "Yesod", "Malchut" },
            new[] { "Хесед", "Гвура", "Тиферет", "Нецах", "Ход", "Йесод", "Малхут" },
            new[] { "חסד", "גבורה", "תפארת", "נצח", "הוד", "יסוד", "מלכות" },
        };

        /// <summary>The sefira of a day of the Omer: the day's within the week's ("Gevurah she'b'Chesed" for day 2); "" outside it.</summary>
        public static string OmerSefira(int day, string language)
        {
            if (day < 1 || day > 49) return "";
            bool hebrew = language == "עברית", slavic = language == "русский" || language == "беларуская";
            string[] names = Sefirot[hebrew ? 2 : slavic ? 1 : 0];
            string ofDay = names[(day - 1) % 7], ofWeek = names[(day - 1) / 7];
            return hebrew ? ofDay + " שב" + ofWeek : slavic ? ofDay + " ше-бе-" + ofWeek : ofDay + " she'b'" + ofWeek;
        }

        /// <summary>
        /// The candles of Hanukkah besides the shamash: 1 on 25 Kislev to 8 on 2 or 3 Tevet; 0 outside it or outside the
        /// Hebrew calendar. They are lit on the evening that begins the day.
        /// </summary>
        public static int ChanukahCandles(DateTime date)
        {
            date = date.Date;
            if (date < Calendar.MinSupportedDateTime.AddYears(1) || date > Calendar.MaxSupportedDateTime.AddYears(-1)) return 0;
            int year = Calendar.GetYear(date), month = MonthNumber(date);
            if (month != Kislev && month != Tevet) return 0;
            int candles = (date - Day(year, Kislev, 25)).Days + 1;
            return candles >= 1 && candles <= 8 ? candles : 0;
        }

        // The .NET month of a month of the numbering above in a given year.
        private static int NetMonth(int year, int month) => !Calendar.IsLeapYear(year) && month >= 7 ? month - 1 : month;

        private static DateTime Day(int year, int month, int day) => Calendar.ToDateTime(year, NetMonth(year, month), day, 0, 0, 0, 0);

        /// <summary>
        /// The special days of the date, in Israel or in the diaspora; none outside the range of the Hebrew calendar.
        /// A date has the time of day dropped.
        /// </summary>
        public static IReadOnlyList<JewishDay> Days(DateTime date, bool israel)
        {
            date = date.Date;
            var days = new List<JewishDay>();
            if (date < Calendar.MinSupportedDateTime.AddYears(2) || date > Calendar.MaxSupportedDateTime.AddYears(-2)) return days;
            int year = Calendar.GetYear(date), month = MonthNumber(date), day = Calendar.GetDayOfMonth(date);
            DayOfWeek weekday = date.DayOfWeek;

            switch (month)
            {
                case Tishrei:
                    if (day <= 2) days.Add(new JewishDay("RoshHashana", DayKind.YomTov, day, 2));
                    if (day == 3 && weekday != DayOfWeek.Saturday || day == 4 && weekday == DayOfWeek.Sunday)
                        days.Add(new JewishDay("FastOfGedaliah", DayKind.Fast, 1, 1));
                    if (day == 10) days.Add(new JewishDay("YomKippur", DayKind.YomTov, 1, 1));
                    if (day >= 15 && day <= 21)
                    {
                        bool yomTov = day == 15 || !israel && day == 16;
                        string key = day == 21 ? "HoshanaRabba" : yomTov ? "Sukkot" : "CholHamoedSukkot";
                        days.Add(new JewishDay(key, yomTov ? DayKind.YomTov : DayKind.CholHamoed, day - 14, 7));
                    }
                    if (day == 22) days.Add(new JewishDay("SheminiAtzeret", DayKind.YomTov, 1, 1));
                    if (day == 22 && israel || day == 23 && !israel) days.Add(new JewishDay("SimchatTorah", DayKind.YomTov, 1, 1));
                    break;
                case Kislev:
                    if (day >= 25) days.Add(new JewishDay("Hanukkah", DayKind.Minor, day - 24, 8));
                    break;
                case Tevet:
                    int hanukkah = Calendar.GetDaysInMonth(year, NetMonth(year, Kislev)) - 24 + day;
                    if (hanukkah <= 8) days.Add(new JewishDay("Hanukkah", DayKind.Minor, hanukkah, 8));
                    if (day == 10) days.Add(new JewishDay("FastOfTevet", DayKind.Fast, 1, 1));
                    break;
                case Shevat:
                    if (day == 15) days.Add(new JewishDay("TuBishvat", DayKind.Minor, 1, 1));
                    break;
                case AdarI:
                    if (day == 14) days.Add(new JewishDay("PurimKatan", DayKind.Minor, 1, 1));
                    break;
                case Adar:
                    if (day == 13 && weekday != DayOfWeek.Saturday || day == 11 && weekday == DayOfWeek.Thursday)
                        days.Add(new JewishDay("FastOfEsther", DayKind.Fast, 1, 1));
                    if (day == 14) days.Add(new JewishDay("Purim", DayKind.Minor, 1, 1));
                    if (day == 15) days.Add(new JewishDay("ShushanPurim", DayKind.Minor, 1, 1));
                    break;
                case Nisan:
                    if (day >= 15 && day <= (israel ? 21 : 22))
                    {
                        bool yomTov = day == 15 || day == 21 || !israel && (day == 16 || day == 22);
                        days.Add(new JewishDay(yomTov ? "Pesach" : "CholHamoedPesach", yomTov ? DayKind.YomTov : DayKind.CholHamoed, day - 14, israel ? 7 : 8));
                    }
                    if (day == YomHashoahDay(year)) days.Add(new JewishDay("YomHashoah", DayKind.Minor, 1, 1));
                    break;
                case Iyar:
                    int independence = YomHaatzmautDay(year);
                    if (day == independence - 1) days.Add(new JewishDay("YomHazikaron", DayKind.Minor, 1, 1));
                    if (day == independence) days.Add(new JewishDay("YomHaatzmaut", DayKind.Minor, 1, 1));
                    if (day == 14) days.Add(new JewishDay("PesachSheni", DayKind.Minor, 1, 1));
                    if (day == 18) days.Add(new JewishDay("LagBaomer", DayKind.Minor, 1, 1));
                    if (day == 28) days.Add(new JewishDay("YomYerushalayim", DayKind.Minor, 1, 1));
                    break;
                case Sivan:
                    if (day == 6 || day == 7 && !israel) days.Add(new JewishDay("Shavuot", DayKind.YomTov, day - 5, israel ? 1 : 2));
                    break;
                case Tammuz:
                    if (day == 17 && weekday != DayOfWeek.Saturday || day == 18 && weekday == DayOfWeek.Sunday)
                        days.Add(new JewishDay("FastOfTammuz", DayKind.Fast, 1, 1));
                    break;
                case Av:
                    if (day == 9 && weekday != DayOfWeek.Saturday || day == 10 && weekday == DayOfWeek.Sunday)
                        days.Add(new JewishDay("TishaBeav", DayKind.Fast, 1, 1));
                    if (day == 15) days.Add(new JewishDay("TuBeav", DayKind.Minor, 1, 1));
                    break;
            }

            // Rosh Chodesh: the first of the month, and the thirtieth of a month of 30 days before it.
            if (day == 30)
            {
                DateTime next = date.AddDays(1);
                days.Add(new JewishDay("RoshChodesh", DayKind.RoshChodesh, 1, 2, MonthNumber(next), Calendar.IsLeapYear(Calendar.GetYear(next))));
            }
            else if (day == 1 && month != Tishrei)
            {
                bool twoDays = Calendar.GetDayOfMonth(date.AddDays(-1)) == 30;
                days.Add(new JewishDay("RoshChodesh", DayKind.RoshChodesh, twoDays ? 2 : 1, twoDays ? 2 : 1, month, Calendar.IsLeapYear(year)));
            }
            return days;
        }

        // Yom HaShoah is the 27th of Nisan, moved off the Friday (to Thursday) and off the Sunday (to Monday).
        private static int YomHashoahDay(int year) => Day(year, Nisan, 27).DayOfWeek switch
        {
            DayOfWeek.Friday => 26,
            DayOfWeek.Sunday => 28,
            _ => 27,
        };

        // Yom HaAtzmaut is the 5th of Iyar, moved to Thursday when it falls on a Friday or Saturday, to Tuesday when on
        // a Monday; Yom HaZikaron is the day before.
        private static int YomHaatzmautDay(int year) => Day(year, Iyar, 5).DayOfWeek switch
        {
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 3,
            DayOfWeek.Monday => 6,
            _ => 5,
        };
    }
}
