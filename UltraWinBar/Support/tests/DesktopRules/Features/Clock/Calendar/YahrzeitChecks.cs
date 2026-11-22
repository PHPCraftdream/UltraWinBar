using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltraWinBar.Utilities;

// The yahrzeits of well-known Torah figures: the list, its categories, and the days.
internal static class YahrzeitChecks
{
    private static readonly HebrewCalendar Calendar = new HebrewCalendar();

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        CheckList();
        CheckDays();
        CheckSelection();
    }

    private static DateTime Day(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckList()
    {
        var all = Yahrzeits.All;
        Check(all.Count == 46, $"The list has 46 figures, has {all.Count}.");
        Check(all.Select(person => person.Key).Distinct().Count() == all.Count, "Every figure has a key of its own.");
        Check(Yahrzeits.Categories.Count == 8 && Yahrzeits.Categories.Distinct().Count() == 8, "Eight categories.");
        // A month has at most this many days (Cheshvan and Kislev vary); the month numbering has no 6 in a plain year, and Adar I has no yahrzeits.
        var monthLength = new Dictionary<int, int> { [1] = 30, [2] = 30, [3] = 30, [4] = 29, [5] = 30, [7] = 29, [8] = 30, [9] = 29, [10] = 30, [11] = 29, [12] = 30, [13] = 29 };
        foreach (var person in all)
        {
            Check(Yahrzeits.Categories.Contains(person.Category), $"{person.Key}: unknown category {person.Category}.");
            Check(monthLength.TryGetValue(person.Month, out int length) && person.Day >= 1 && person.Day <= length, $"{person.Key}: {person.Day}/{person.Month} is not a day of the Hebrew calendar.");
            var names = new[] { "English", "русский", "עברית" }.Select(person.Name).ToArray();
            Check(names.All(name => !string.IsNullOrWhiteSpace(name)) && names.Distinct().Count() == 3, $"{person.Key} needs three different names.");
            Check(person.Name("Deutsch") == names[0], $"{person.Key}: a language without names falls back to English.");
        }
        foreach (string category in Yahrzeits.Categories)
        {
            var names = new[] { "English", "русский", "עברית" }.Select(language => Yahrzeits.CategoryName(category, language)).ToArray();
            Check(names.All(name => !string.IsNullOrWhiteSpace(name)) && names.Distinct().Count() == 3, $"{category} needs three different names.");
            Check(Yahrzeits.CategoryName(category, "Deutsch") == names[0], $"{category}: a language without names falls back to English.");
            Check(Yahrzeits.OfCategory(category).Count() >= 3, $"{category} has fewer than three figures.");
        }
        Check(Yahrzeits.Categories.Sum(category => Yahrzeits.OfCategory(category).Count()) == all.Count, "Every figure is in one category.");
        Console.WriteLine($"PASS: {all.Count} figures in {Yahrzeits.Categories.Count} categories, each with a key of its own, a valid day of the Hebrew calendar, and names in English, Russian and Hebrew.");
    }

    private static void CheckDays()
    {
        // Documented dates of death, as the Gregorian date (dates given in the Julian calendar converted): the traditional day
        // is the Hebrew day of that date or, for a death after sunset, the next one (the last number of a line). Those before 1583, whom the Hebrew calendar of the
        // runtime does not reach (Rashi, Rambam, Ramban, Rabbeinu Tam, Rabbi Yosef Karo, the Arizal, the Ramak), were checked
        // when the list was made against a calendar that reaches them (as the Julian date, 1105 to 1575).
        var documented = new (string Key, string Death, int Evening)[]
        {
            ("maharal", "1609-09-17", 0),
            ("ohr_hachaim", "1743-07-07", 0),
            ("ramchal", "1746-05-16", 0),
            ("baba_sali", "1984-01-08", 0),
            ("ben_ish_chai", "1909-08-30", 0),
            ("maggid", "1772-12-15", 0),
            ("elimelech", "1787-03-11", 0),
            ("alter_rebbe", "1812-12-27", 0),
            ("levi_yitzchak", "1809-10-05", 0),
            ("nachman", "1810-10-16", 0),
            ("chozeh", "1815-08-15", 0),
            ("mittler", "1827-11-28", 0),
            ("tzemach", "1866-03-29", 0),
            ("maharash", "1882-09-26", 0),
            ("rayatz", "1950-01-28", 0),
            ("rebbe", "1994-06-12", 0),
            ("vilna_gaon", "1797-10-09", 0),
            ("chaim_volozhin", "1821-06-14", 0),
            ("akiva_eiger", "1837-10-12", 0),
            ("chasam_sofer", "1839-10-03", 0),
            ("salanter", "1883-02-02", 0),
            ("chafetz_chaim", "1933-09-15", 0),
            ("rav_kook", "1935-09-01", 0),
            ("chazon_ish", "1953-10-24", 0),
            ("moshe_feinstein", "1986-03-23", 1),
            ("ovadia", "2013-10-07", 0),
        };
        foreach (var (key, death, evening) in documented)
        {
            var found = Yahrzeits.On(Day(death).AddDays(evening));
            Check(found.Any(person => person.Key == key), $"{key} died on {death}{(evening == 1 ? " in the evening" : "")}: that is not his yahrzeit by the Hebrew calendar.");
        }
        Console.WriteLine($"PASS: {documented.Length} yahrzeits fall on the documented dates of death (one a day later, for a death after sunset).");

        // Traditional days that the festivals fix: Rashbi on Lag BaOmer, King David and the Baal Shem Tov on Shavuot, the Rambam on 20 Tevet,
        // Moshe on 7 Adar, and in a leap year on 7 Adar II and not on 7 Adar I.
        Check(Yahrzeits.On(Day("2026-05-05")).Any(person => person.Key == "rashbi"), "Rashbi is kept on Lag BaOmer.");
        Check(new[] { "david", "baal_shem_tov" }.All(key => Yahrzeits.On(Day("2026-05-22")).Any(person => person.Key == key)), "David and the Baal Shem Tov are kept on Shavuot.");
        Check(Yahrzeits.On(Day("2026-01-09")).Any(person => person.Key == "rambam"), "The Rambam is kept on 20 Tevet.");
        Check(Yahrzeits.On(Day("2026-02-24")).Any(person => person.Key == "moshe"), "Moshe is kept on 7 Adar.");
        Check(Yahrzeits.On(Day("2024-03-17")).Any(person => person.Key == "moshe") && Yahrzeits.On(Day("2024-02-16")).All(person => person.Key != "moshe"),
            "In a leap year Moshe is kept on 7 Adar II, not on 7 Adar I.");

        // In every year from 5780 to 5800, plain and leap, each figure has a day, once.
        for (int year = 5780; year <= 5800; year++)
        {
            var counts = new Dictionary<string, int>();
            for (DateTime day = Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0); day < Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0); day = day.AddDays(1))
                foreach (var person in Yahrzeits.On(day)) counts[person.Key] = counts.GetValueOrDefault(person.Key) + 1;
            var wrong = Yahrzeits.All.Where(person => counts.GetValueOrDefault(person.Key) != 1).Select(person => person.Key).ToArray();
            Check(wrong.Length == 0, $"{year}: not once a year: {string.Join(", ", wrong)}.");
        }
        Console.WriteLine("PASS: Rashbi, David, the Baal Shem Tov, the Rambam and Moshe fall on their festivals and months (Adar II in a leap year), and in 21 years each of the figures has exactly one day a year.");
    }

    private static void CheckSelection()
    {
        // The figures that are hidden do not show; the others do. The time of day does not matter; outside the calendar there is nothing.
        var onRashbi = Yahrzeits.On(Day("2026-05-05"));
        Check(onRashbi.Count >= 1, "Someone has a yahrzeit on Lag BaOmer.");
        Check(Yahrzeits.On(Day("2026-05-05"), new HashSet<string> { "rashbi" }).All(person => person.Key != "rashbi") &&
            Yahrzeits.On(Day("2026-05-05"), new List<string>()).Count == onRashbi.Count && Yahrzeits.On(Day("2026-05-05"), new[] { "moshe" }).Count == onRashbi.Count,
            "A hidden figure does not show, others do.");
        Check(Yahrzeits.On(Day("2026-05-05").AddHours(22)).Count == onRashbi.Count, "The time of day does not matter.");
        Check(Yahrzeits.On(new DateTime(1500, 1, 1)).Count == 0 && Yahrzeits.On(new DateTime(2300, 1, 1)).Count == 0, "Nothing outside the Hebrew calendar.");
        var everyone = Yahrzeits.All.Select(person => person.Key).ToArray();
        for (DateTime day = Day("2026-01-01"); day < Day("2027-01-01"); day = day.AddDays(1))
            Check(Yahrzeits.On(day, everyone).Count == 0, $"{day:yyyy-MM-dd}: all hidden still shows someone.");
        Console.WriteLine("PASS: hiding figures by key removes exactly them; the time of day and dates outside the calendar are handled.");
    }
}
