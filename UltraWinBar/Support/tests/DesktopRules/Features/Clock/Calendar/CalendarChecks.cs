using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltraWinBar.Utilities;

// The Jewish calendar beyond the date: which Torah portion is read on which Shabbat.
internal static class CalendarChecks
{
    private static readonly HebrewCalendar Calendar = new HebrewCalendar();

    // Month numbers of the .NET Hebrew calendar: Tishrei is 1; in a leap year Adar I and Adar II take 6 and 7 and
    // every later month moves up by one.
    private static int Nisan(int year) => Calendar.IsLeapYear(year) ? 8 : 7;
    private static int Sivan(int year) => Nisan(year) + 2;
    private static int Av(int year) => Nisan(year) + 4;

    private static DateTime Day(int year, int month, int day) => Calendar.ToDateTime(year, month, day, 0, 0, 0, 0);

    private static DateTime NextSaturday(DateTime date)
    {
        while (date.DayOfWeek != DayOfWeek.Saturday) date = date.AddDays(1);
        return date;
    }

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        CheckTorahReadings();
    }

    private static string Token(TorahReading reading) => reading == null ? "_" : reading.IsDouble ? $"{reading.First}-{reading.Second}" : reading.First.ToString(CultureInfo.InvariantCulture);

    private static void CheckTorahReadings()
    {
        // One whole year of each of the 14 kinds, in the diaspora and in Israel, as an independent calendar (pyluach,
        // MIT, used only as a reference for the expected values) reads them from the Shabbat after Simchat Torah on.
        // "21-22" is two portions read together, "_" a Shabbat with a festival's reading.
        var wholeYears = new (int Year, bool Israel, string Expected)[]
        {
            (5780, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 _ 34 35 36 37 38-39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5780, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5781, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5781, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5782, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5782, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 _"),
            (5784, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5784, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5785, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5785, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5786, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 _ 34 35 36 37 38-39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5786, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5787, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 _ 34 35 36 37 38-39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5787, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5788, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5788, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5789, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5789, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5790, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 _ 34 35 36 37 38-39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5790, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 _ 52 _ _"),
            (5795, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 _ 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5795, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 _ 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5797, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5797, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21-22 23 24 _ 25 26-27 28-29 30 31-32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50-51 52 _ _"),
            (5803, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41-42 43 44 45 46 47 48 49 50 51 52 _"),
            (5803, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 _ 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 _"),
            (5812, false, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 _ 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 _"),
            (5812, true, "0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 _ 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 _"),
        };
        foreach (var (year, israel, expected) in wholeYears)
        {
            string actual = string.Join(" ", TorahReadings.Year(year, israel).Select(item => Token(item.Reading)));
            if (actual != expected) throw new Exception($"{year} ({(israel ? "Israel" : "diaspora")}): expected\n{expected}\ngot\n{actual}");
        }
        Console.WriteLine($"PASS: {wholeYears.Length} whole years (every kind of year, diaspora and Israel) are read the way the reference calendar reads them.");

        // Single Shabbatot picked at random over 21 years, found by date: this also covers the turn of the year.
        var spotChecks = new (string Date, bool Israel, string Expected)[]
        {
            ("2020-01-25", false, "13"),
            ("2020-02-15", false, "16"),
            ("2020-04-04", true, "24"),
            ("2020-07-18", true, "41-42"),
            ("2020-08-01", false, "44"),
            ("2020-11-28", false, "6"),
            ("2021-01-09", true, "12"),
            ("2021-01-16", false, "13"),
            ("2021-04-03", true, "_"),
            ("2021-04-24", false, "28-29"),
            ("2021-09-04", true, "50"),
            ("2021-10-02", true, "0"),
            ("2022-01-08", false, "14"),
            ("2022-03-05", true, "22"),
            ("2022-03-12", true, "23"),
            ("2022-12-17", true, "8"),
            ("2023-02-11", true, "16"),
            ("2023-04-15", true, "25"),
            ("2023-06-24", false, "37"),
            ("2023-08-12", true, "46"),
            ("2023-09-02", false, "49"),
            ("2023-11-25", false, "6"),
            ("2023-12-16", true, "9"),
            ("2024-01-27", false, "15"),
            ("2024-03-30", true, "24"),
            ("2024-04-06", false, "25"),
            ("2024-08-03", false, "41-42"),
            ("2024-10-19", true, "_"),
            ("2025-03-22", true, "21"),
            ("2025-04-12", true, "24"),
            ("2025-04-19", false, "_"),
            ("2025-08-16", true, "45"),
            ("2026-02-28", false, "19"),
            ("2026-08-08", false, "46"),
            ("2026-08-08", true, "46"),
            ("2026-10-24", false, "2"),
            ("2026-11-14", true, "5"),
            ("2026-11-28", false, "7"),
            ("2027-05-08", true, "29"),
            ("2027-07-03", false, "36"),
            ("2027-12-11", true, "6"),
            ("2028-01-15", true, "11"),
            ("2028-01-29", false, "13"),
            ("2028-01-29", true, "13"),
            ("2028-04-29", false, "26-27"),
            ("2028-06-10", true, "35"),
            ("2028-07-29", true, "43"),
            ("2028-11-04", false, "3"),
            ("2029-01-13", false, "13"),
            ("2029-01-13", true, "13"),
            ("2029-01-27", false, "15"),
            ("2029-02-17", true, "18"),
            ("2029-04-14", true, "26-27"),
            ("2029-06-23", false, "38"),
            ("2029-07-07", false, "40"),
            ("2029-09-01", true, "49"),
            ("2030-04-20", false, "_"),
            ("2030-05-25", false, "32"),
            ("2030-07-06", true, "38"),
            ("2031-07-12", false, "40"),
            ("2031-12-13", true, "9"),
            ("2032-01-31", false, "16"),
            ("2032-03-06", true, "21-22"),
            ("2032-05-29", false, "35"),
            ("2033-05-28", true, "33"),
            ("2033-09-17", true, "50-51"),
            ("2033-12-10", false, "7"),
            ("2034-05-27", false, "34"),
            ("2034-07-15", false, "41-42"),
            ("2034-07-15", true, "41-42"),
            ("2034-12-02", false, "8"),
            ("2034-12-30", true, "12"),
            ("2035-04-21", false, "28"),
            ("2035-06-30", false, "37"),
            ("2035-09-08", false, "47"),
            ("2036-01-12", true, "11"),
            ("2036-01-19", false, "12"),
            ("2036-03-01", false, "18"),
            ("2036-04-05", false, "24"),
            ("2036-04-26", true, "26-27"),
            ("2036-10-25", true, "1"),
            ("2037-07-04", false, "40"),
            ("2038-02-20", true, "20"),
            ("2038-09-18", true, "49"),
            ("2039-02-12", true, "16"),
            ("2039-06-18", false, "36"),
            ("2039-08-20", false, "46"),
            ("2040-03-17", true, "23"),
            ("2040-04-21", false, "28-29"),
            ("2040-05-19", true, "34"),
        };
        foreach (var (date, israel, expected) in spotChecks)
        {
            string actual = Token(TorahReadings.ForShabbat(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), israel));
            if (actual != expected) throw new Exception($"{date} ({(israel ? "Israel" : "diaspora")}): expected portion {expected}, got {actual}.");
        }
        Console.WriteLine($"PASS: {spotChecks.Length} Shabbatot read the way an independent calendar reads them.");

        // The fixed points of the calendar, in every year from 5700 to 5900, in the diaspora and in Israel. They do not
        // depend on the tables: every portion is read once and in order; Bamidbar before Shavuot, Devarim on the
        // Shabbat of or before the ninth of Av, Nitzavim before Rosh Hashana, Haazinu between Rosh Hashana and Sukkot;
        // a Shabbat is without a portion of its own exactly when a festival falls on it.
        for (int year = 5700; year <= 5900; year++)
        foreach (bool israel in new[] { false, true })
        {
            var shabbatot = TorahReadings.Year(year, israel).ToArray();
            DateTime next = TorahReadings.FirstShabbat(year + 1, israel);
            if (shabbatot[0].Shabbat.AddDays(7 * shabbatot.Length) != next)
                throw new Exception($"{year} ({(israel ? "Israel" : "diaspora")}): the schedule has {shabbatot.Length} Shabbatot, the year has {(next - shabbatot[0].Shabbat).Days / 7}.");

            var order = new List<int>();
            foreach (var (shabbat, reading) in shabbatot)
            {
                if (reading != null)
                {
                    order.Add(reading.First);
                    if (reading.IsDouble) order.Add(reading.Second);
                }
                Check((reading == null) == IsFestival(shabbat, israel),$"{year}/{israel}: {shabbat:yyyy-MM-dd} {Token(reading)} and the festival of that day disagree");
            }
            Check(order.SequenceEqual(Enumerable.Range(0, 53)), $"{year}/{israel}: the portions are not read once and in order");

            DateTime Of(int portion) => shabbatot.First(item => item.Reading != null && (item.Reading.First == portion || item.Reading.Second == portion)).Shabbat;
            DateTime shavuot = Day(year, Sivan(year), 6), tishaBeAv = Day(year, Av(year), 9);
            DateTime roshHashana = Day(year + 1, 1, 1), sukkot = Day(year + 1, 1, 15);
            Check(Of(33) < shavuot && (shavuot - Of(33)).Days <= 7, $"{year}/{israel}: Bamidbar is not the Shabbat before Shavuot");
            Check(Of(43) <= tishaBeAv && (tishaBeAv - Of(43)).Days < 7, $"{year}/{israel}: Devarim is not the Shabbat of or before the ninth of Av");
            Check(Of(50) < roshHashana && (roshHashana - Of(50)).Days <= 7, $"{year}/{israel}: Nitzavim is not the Shabbat before Rosh Hashana");
            Check(Of(52) > roshHashana && Of(52) < sukkot, $"{year}/{israel}: Haazinu is not between Rosh Hashana and Sukkot");
        }
        Console.WriteLine("PASS: in 402 schedules (201 years, diaspora and Israel) every portion is read once and in order, Bamidbar comes before Shavuot, Devarim on or before the ninth of Av, Nitzavim before Rosh Hashana, Haazinu before Sukkot, and festival Shabbatot are exactly the ones without a portion.");

        // Names, in three languages; two portions read together are joined by a hyphen.
        foreach (string language in new[] { "English", "русский", "עברית", "Deutsch" })
        {
            var names = TorahReadings.NamesIn(language);
            if (names.Count != 54 || names.Any(string.IsNullOrWhiteSpace) || names.Distinct().Count() != 54)
                throw new Exception($"54 different names are needed in {language}.");
        }
        var pair = new TorahReading(21, 22);
        if (pair.Name("English") != "Vayakhel-Pekudei" || pair.Name("עברית") != "ויקהל-פקודי" || pair.Name("русский") != "Ваякхель-Пекудей" ||
            new TorahReading(0, -1).Name("Deutsch") != "Bereshit" || TorahReadings.NamesIn("русский")[52] != "Гаазину" || TorahReadings.NamesIn("עברית")[53] != "וזאת הברכה")
            throw new Exception("Portion names are wrong.");
        // Only a Shabbat has a portion, and only inside the range of the Hebrew calendar.
        if (TorahReadings.ForShabbat(new DateTime(2026, 11, 20), false) != null || TorahReadings.ForShabbat(new DateTime(2026, 11, 22), false) != null ||
            TorahReadings.ForShabbat(NextSaturday(new DateTime(1500, 1, 2)), false) != null || TorahReadings.ForShabbat(NextSaturday(new DateTime(2300, 1, 1)), true) != null)
            throw new Exception("Only Shabbatot of the calendar's range have a portion.");
        if (TorahReadings.ForShabbat(new DateTime(2026, 11, 21, 18, 30, 0), false)?.Name("English") != "Vayetze")
            throw new Exception("The time of day must not matter: 21 November 2026 is Shabbat Vayetze.");
        Console.WriteLine("PASS: the portions have names in English, Russian and Hebrew; only a Shabbat in the calendar's range has a portion.");
    }

    // Festival days on which the Torah reading is the festival's: Rosh Hashana, Yom Kippur, Sukkot with Shemini
    // Atzeret and Simchat Torah, Pesach, Shavuot; the diaspora keeps one more day of each of the last three.
    private static bool IsFestival(DateTime date, bool israel)
    {
        int year = Calendar.GetYear(date), month = Calendar.GetMonth(date), day = Calendar.GetDayOfMonth(date);
        if (month == 1) return day == 1 || day == 2 || day == 10 || (day >= 15 && day <= (israel ? 22 : 23));
        if (month == Nisan(year)) return day >= 15 && day <= (israel ? 21 : 22);
        if (month == Sivan(year)) return day == 6 || (!israel && day == 7);
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
