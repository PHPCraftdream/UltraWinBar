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
        Check(all.Count == 142, $"The list has 142 figures, has {all.Count}.");
        Check(all.Select(person => person.Key).Distinct().Count() == all.Count, "Every figure has a key of its own.");
        Check(Yahrzeits.Categories.Count == 12 && Yahrzeits.Categories.Distinct().Count() == 12, "Twelve categories.");
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
            // The figures added later, each day also confirmed by two or more sources.
            ("baal_hasulam", "1954-10-07", 0), ("rabash", "1991-09-13", 0), ("rav_berg", "2013-09-16", 0), ("rashash", "1777-01-18", 0),
            ("kaduri", "2006-01-28", 1), ("nosson", "1844-12-20", 0), ("odesser", "1994-10-23", 0), ("chaim_brisker", "1918-07-30", 0),
            ("brisker_rav", "1959-10-11", 0), ("netziv", "1893-08-10", 0), ("yitzchak_elchanan", "1896-03-06", 0), ("chaim_ozer", "1940-08-09", 0),
            ("alter_slabodka", "1927-01-31", 1), ("aharon_kotler", "1962-11-29", 0), ("steipler", "1985-08-10", 0), ("shach", "2001-11-02", 0),
            ("elyashiv", "2012-07-18", 0), ("chaim_kanievsky", "2022-03-18", 0), ("chida", "1806-03-01", 0), ("abir_yaakov", "1880-01-04", 0),
            ("kaf_hachaim", "1939-05-27", 0), ("ben_zion_abba_shaul", "1998-07-13", 0), ("mordechai_eliyahu", "2010-06-07", 0), ("kafih", "2000-07-21", 0),
            ("chiddushei_harim", "1866-03-10", 0), ("sfas_emes", "1905-01-11", 0), ("sar_shalom", "1855-09-10", 0), ("divrei_yoel", "1979-08-19", 0),
            ("divrei_chaim", "1876-04-19", 0), ("ruzhin", "1850-10-09", 0), ("bnei_yissaschar", "1841-01-11", 0), ("apter_rav", "1825-03-24", 0),
            ("kozhnitz", "1814-09-28", 0), ("yid_hakadosh", "1813-10-13", 0), ("simcha_bunim", "1827-09-04", 0), ("meor_einayim", "1797-10-31", 0),
            ("brandwein", "1969-04-06", 0),
            // The third set.
            ("noda_biyehuda", "1793-04-29", 0), ("shelah", "1630-03-24", 0), ("malbim", "1879-09-18", 0), ("karlin", "1772-04-22", 0),
            ("ropshitz", "1827-05-08", 0), ("zidichov", "1831-06-22", 0), ("imrei_emes", "1948-06-13", 0), ("belz_aharon", "1957-08-18", 0),
            ("klausenburg", "1994-06-18", 0), ("bobov", "2000-08-02", 0), ("shkop", "1939-10-22", 0), ("meltzer", "1953-11-17", 0),
            ("shmulevitz", "1979-01-02", 0), ("soloveitchik_rav", "1993-04-09", 0), ("chaim_pinto", "1845-09-28", 0), ("uziel", "1953-09-04", 0),
            ("aryeh_levin", "1969-03-28", 0), ("tzvi_yehuda", "1982-03-09", 0),
            // The fourth set.
            ("pnei_yehoshua", "1756-01-16", 0), ("eybeschutz", "1764-09-18", 0), ("yaavetz", "1776-04-19", 0), ("chayei_adam", "1820-09-12", 0),
            ("ksav_sofer", "1871-12-31", 0), ("hirsch", "1888-12-31", 0), ("degel", "1800-05-12", 0), ("rimanov", "1815-05-29", 0),
            ("shmelke", "1778-04-28", 0), ("sassov", "1807-01-13", 0), ("tzadok", "1900-09-03", 0), ("belz_yissachar_dov", "1926-10-29", 1),
            ("beis_yisrael", "1977-02-20", 0), ("shteinman", "2017-12-12", 0), ("palagi", "1868-02-10", 0), ("tzadka", "1991-10-20", 0),
            // The fifth set.
            ("chacham_tzvi", "1718-05-02", 0), ("haflaah", "1805-07-01", 0), ("nesivos", "1832-05-25", 0), ("aruch_hashulchan", "1908-03-25", 0),
            ("uri_strelisk", "1826-09-25", 0), ("yismach_moshe", "1841-07-17", 0), ("tiferes_shlomo", "1866-03-16", 0), ("yehoshua_belz", "1894-01-30", 0),
            ("meir_simcha", "1926-08-14", 0), ("novardok", "1919-12-09", 0), ("rogatchover", "1936-03-05", 0), ("elya_lopian", "1970-09-21", 0),
            ("yitzchak_nissim", "1981-08-09", 0),
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
