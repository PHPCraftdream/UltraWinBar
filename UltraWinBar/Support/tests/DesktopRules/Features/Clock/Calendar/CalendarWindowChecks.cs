using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UltraWinBar;
using UltraWinBar.Utilities;

// The calendar window: what it says about a day and a month, the tree of yahrzeits of its settings, and the windows themselves.
internal static class CalendarWindowChecks
{
    internal static void Run(string[] args, DirectoryInfo repositoryRoot)
    {
        var english = EnglishStrings(repositoryRoot);
        CheckDetails(english);
        CheckMonth(english);
        CheckTree();
        CheckWindows(repositoryRoot, english);
    }

    private static DateTime Day(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static Dictionary<string, string> Strings(string path) => Regex.Matches(File.ReadAllText(path), "<s:String x:Key=\"([^\"]+)\">(.*?)</s:String>", RegexOptions.Singleline)
        .Cast<Match>().ToDictionary(match => match.Groups[1].Value, match => System.Net.WebUtility.HtmlDecode(match.Groups[2].Value));

    private static Dictionary<string, string> EnglishStrings(DirectoryInfo root) =>
        Strings(Path.Combine(root.FullName, "UltraWinBar", "Assets", "Languages", "Group01", "English.xaml"));

    private static readonly HebrewPlace Jerusalem = new HebrewPlace
    {
        Name = "Jerusalem", Region = "Jerusalem", Country = "Israel", CountryCode = "IL", Latitude = 31.778, Longitude = 35.235, TimeZoneId = "Asia/Jerusalem",
    };

    private static readonly HebrewPlace London = new HebrewPlace
    {
        Name = "London", Region = "England", Country = "United Kingdom", CountryCode = "GB", Latitude = 51.5074, Longitude = -0.1278, TimeZoneId = "Europe/London",
    };

    private static void CheckDetails(Dictionary<string, string> english)
    {
        Func<string, string> text = key => english.TryGetValue(key, out string value) ? value : throw new Exception($"English lacks the string {key}.");
        Check(DayDetailsBuilder.IsLandOfIsrael(Jerusalem) && DayDetailsBuilder.IsLandOfIsrael(new HebrewPlace { CountryCode = "PS" }) &&
            !DayDetailsBuilder.IsLandOfIsrael(London) && !DayDetailsBuilder.IsLandOfIsrael(null), "Israel and its neighbour in the place data are the Land of Israel; no place is not.");
        Check(DayDetailsBuilder.CultureOf("русский").Name == "ru-RU" && DayDetailsBuilder.CultureOf("עברית").Name == "he-IL" && DayDetailsBuilder.CultureOf("English").Name == "en-US" &&
            DayDetailsBuilder.CultureOf("Deutsch").Name == CultureInfo.CurrentCulture.Name, "The culture follows the language of the settings.");

        // A Shabbat in Jerusalem: its date, the portion, the times of the day with the end of the rest, and the molad of the coming month.
        var shabbat = DayDetailsBuilder.Describe(Day("2026-11-21"), Jerusalem, "English", null, text);
        Check(shabbat.HebrewDate == "11 Kislev 5787" && shabbat.Heading == "Saturday, November 21, 2026", $"The Hebrew date of the day: {shabbat.HebrewDate}, {shabbat.Heading}.");
        Check(shabbat.Portion == "Weekly portion: Vayetze" && shabbat.Events.Count == 0 && shabbat.Yahrzeits.Count == 0, "A Shabbat has its portion and no festival.");
        string eleven = new DateTime(2000, 1, 1, 11, 11, 0).ToString("t", new CultureInfo("en-US"));
        Check(shabbat.Molad == $"Molad of Tevet: Wednesday, {eleven} and 4 chalakim", $"The molad of the coming month: {shabbat.Molad}.");
        Check(shabbat.Place.StartsWith("Place: Jerusalem, Jerusalem, Israel · Asia/Jerusalem (UTC"), $"The place and its zone: {shabbat.Place}.");
        Check(shabbat.Place.EndsWith(" · Land of Israel"), $"The place says that the festivals and portions are those of the Land of Israel: {shabbat.Place}.");
        Check(shabbat.Times.Count == 12 && shabbat.Times[2].Name == "Sunrise (Netz)" && shabbat.Times[9].Name == "Sunset (Shkiah)" && shabbat.Times[10].Name == "Nightfall (Tzeit HaKochavim)" &&
            shabbat.Times[11].Name == "End of the day of rest (havdalah)" && shabbat.Times.All(time => Regex.IsMatch(time.Time, @"^\d{1,2}:\d\d\s[AP]M$")), $"The times of the day: {string.Join("; ", shabbat.Times)}.");
        Check(shabbat.Times[11].Time == shabbat.Times[10].Time, "Havdalah is at nightfall.");
        Check(shabbat.Times[11].Rest && shabbat.Times.Take(11).All(time => !time.Rest), "Only the havdalah is marked as a line of the day of rest.");
        Check(shabbat.RestStarts != null && shabbat.RestStarts.StartsWith("Begins: Fri ") && shabbat.RestEnds != null && shabbat.RestEnds.StartsWith("Ends: Sat "), $"A day of rest begins on Friday and ends on Saturday: {shabbat.RestStarts}, {shabbat.RestEnds}.");

        // A weekday has the portion of the coming Shabbat, the evening's candles and no end of rest; a Friday has the candles.
        var thursday = DayDetailsBuilder.Describe(Day("2026-11-19"), Jerusalem, "English", null, text);
        Check(thursday.Portion == "Weekly portion: Vayetze" && thursday.RestStarts == null && thursday.RestEnds == null && thursday.Times.Count == 11, "A weekday: the portion, no rest, no evening line.");
        var friday = DayDetailsBuilder.Describe(Day("2026-11-20"), Jerusalem, "English", null, text);
        Check(friday.Times.Count == 12 && friday.Times[11].Name == "Candle lighting" && friday.Times[11].Time != friday.Times[9].Time, "A Friday has the candle lighting before sunset.");

        // Festivals: the days of Pesach are one more in the diaspora than in Israel; the festival's days run from the candles to nightfall.
        var seder = DayDetailsBuilder.Describe(Day("2026-04-02"), London, "English", null, text);
        var sederIsrael = DayDetailsBuilder.Describe(Day("2026-04-02"), Jerusalem, "English", null, text);
        Check(seder.Events.Single() == "Pesach, day 1 of 8" && sederIsrael.Events.Single() == "Pesach, day 1 of 7", "Pesach has 8 days in London and 7 in Jerusalem.");
        Check(seder.RestStarts.StartsWith("Begins: Wed ") && seder.RestEnds.StartsWith("Ends: Sat ") && sederIsrael.RestEnds.StartsWith("Ends: Thu "),
            $"A festival begins on the evening before and ends at nightfall of its last day of rest: {seder.RestStarts} {seder.RestEnds}, Israel {sederIsrael.RestEnds}.");
        Check(seder.Times.Any(time => time.Name == "Candle lighting (after nightfall)"), "The first night of a two-day festival has the candles after nightfall.");
        // No Rosh Chodesh word without the number of days: two days read "day 1 of 2".
        var roshChodesh = DayDetailsBuilder.Describe(Day("2025-12-20"), London, "English", null, text);
        Check(roshChodesh.Events.Any(name => name == "Rosh Chodesh Tevet, day 1 of 2"), $"Rosh Chodesh of two days: {string.Join("; ", roshChodesh.Events)}.");

        // Yahrzeits: those chosen show, in the language of the settings.
        var rambam = DayDetailsBuilder.Describe(Day("2026-01-09"), null, "English", null, text);
        // 20 Tevet is also the Abir Yaakov's: the Rambam, born first, comes first.
        Check(rambam.Yahrzeits.First() == "Rambam (Maimonides)" && DayDetailsBuilder.Describe(Day("2026-01-09"), null, "русский", null, text).Yahrzeits.First() == "Рамбам (Маймонид)" &&
            !DayDetailsBuilder.Describe(Day("2026-01-09"), null, "English", new[] { "rambam" }, text).Yahrzeits.Contains("Rambam (Maimonides)"),
            $"The yahrzeit of the Rambam shows, in the language chosen, unless hidden: {string.Join("; ", rambam.Yahrzeits)}.");

        // With no place: no times, no place line, no start and end of the rest; the rest is the diaspora's.
        var bare = DayDetailsBuilder.Describe(Day("2026-11-21"), null, "English", null, text);
        Check(bare.Place == null && bare.Times.Count == 0 && bare.RestStarts == null && bare.RestEnds == null && bare.Portion == "Weekly portion: Vayetze" && bare.Molad != null, "Without a place the calendar has no times.");
        // A date outside the Hebrew calendar does not break the window.
        var far = DayDetailsBuilder.Describe(new DateTime(2300, 1, 1), Jerusalem, "English", null, text);
        Check(far.HebrewDate == "" && far.Molad == null && far.Portion == null && far.Events.Count == 0, "Far out of range, the details are empty, not a failure.");
        // The other languages: the Hebrew date and the names.
        var hebrew = DayDetailsBuilder.Describe(Day("2026-11-21"), Jerusalem, "עברית", null, text);
        Check(hebrew.HebrewDate == "11 כסלו 5787" && hebrew.Portion.EndsWith("ויצא"), $"In Hebrew: {hebrew.HebrewDate}, {hebrew.Portion}.");
        Console.WriteLine("PASS: the details of a day: Hebrew date, portion, festivals with their day numbers (one more in the diaspora), start and end of the rest, times of the day, candles before or after dark, havdalah, molad, yahrzeits, and none of it when no place is chosen.");
    }

    private static void CheckMonth(Dictionary<string, string> english)
    {
        Func<string, string> text = key => english[key];
        DateTime month = Day("2026-04-01");
        var pesach = DayDetailsBuilder.Cell(Day("2026-04-02"), month, Day("2026-04-02"), false, "English", null, text);
        Check(pesach.GregorianNumber == "2" && pesach.HebrewNumber == "15" && pesach.Marker == "Pesach, day 1 of 8" && !pesach.OutsideMonth && pesach.IsToday && pesach.IsRest && pesach.HasYahrzeit == Yahrzeits.On(Day("2026-04-02")).Count > 0,
            $"The cell of the first day of Pesach: {pesach}.");
        Check(pesach.HebrewLabel == "Nisan, 15" && pesach.GregorianLabel == "2, April", $"The labels of the cell: {pesach.HebrewLabel} / {pesach.GregorianLabel}.");
        var kislev = DayDetailsBuilder.Cell(Day("2026-11-23"), Day("2026-11-01"), Day("2026-11-23"), false, "English", null, text);
        Check(kislev.HebrewLabel == "Kislev, 13" && kislev.GregorianLabel == "23, November", $"The labels of the cell: {kislev.HebrewLabel} / {kislev.GregorianLabel}.");
        var russian = DayDetailsBuilder.Cell(Day("2026-11-23"), Day("2026-11-01"), Day("2026-11-23"), false, "русский", null, text);
        Check(russian.GregorianLabel == "23, ноября" && russian.HebrewLabel.EndsWith(", 13"), $"In Russian the civil month is in the genitive: {russian.GregorianLabel} / {russian.HebrewLabel}.");
        Check(pesach.Tooltip.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Length == 4 && pesach.Tooltip.Contains("15 Nisan 5786") &&
              pesach.Tooltip.Contains("Shevet HaLevi"), $"The tooltip: {pesach.Tooltip}.");
        var shabbat = DayDetailsBuilder.Cell(Day("2026-04-04"), month, Day("2026-04-02"), false, "English", null, text);
        Check(shabbat.Marker == "Chol HaMoed Pesach, day 3 of 8" && shabbat.IsRest && !shabbat.IsToday, "A Shabbat of Chol HaMoed carries the festival, not a portion.");
        var portion = DayDetailsBuilder.Cell(Day("2026-04-18"), month, Day("2026-04-02"), false, "English", null, text);
        Check(portion.Marker.Length > 0 && portion.IsRest && DayDetailsBuilder.Cell(Day("2026-04-15"), month, Day("2026-04-02"), false, "English", null, text).Marker == "", "A Shabbat carries its portion, an ordinary day nothing.");
        // The blocks of a cell: the portion of a Shabbat (not on a festival's), and the special days with their kinds.
        // 18 April 2026 is 1 Iyar: the portion stays, with Rosh Chodesh as its only day.
        Check(portion.Portion != null && portion.Events.All(day => day.Kind == DayKind.RoshChodesh) && shabbat.Portion == null && shabbat.Events.Count == 1 && shabbat.Events[0].Kind == DayKind.CholHamoed &&
              pesach.Events.Count == 1 && pesach.Events[0].Kind == DayKind.YomTov && pesach.Events[0].Name == pesach.Marker,
            $"The blocks of the cells: {portion.Portion} / {shabbat.Events.Count} / {pesach.Events.Count}.");
        // The names as they are usually written (a search engine does not correct them).
        var names = Yahrzeits.All.ToDictionary(person => person.Key);
        Check(names["mittler"].English == "Mitteler Rebbe" && names["mittler"].Russian == "Мителер Ребе" && names["chasam_sofer"].English == "Chatam Sofer" &&
              names["kotzk"].English == "Kotzker Rebbe" && names["meir"].Russian == "Рабби Меир Баал а-Нес", "The names are spelled as usual.");
        // The yahrzeits of a day come eldest first: on 6 Sivan King David (born 2854) before the Baal Shem Tov (5458).
        Check(Yahrzeits.All.All(person => person.Born > 2000 && person.Born < 5800), "Every figure has a year of birth (anno mundi).");
        var sivan6 = Yahrzeits.On(Day("2026-05-22"));
        // The Imrei Emes (5626) passed on Shavuot too: after them.
        Check(sivan6.Select(person => person.Key).SequenceEqual(new[] { "david", "baal_shem_tov", "imrei_emes" }), $"6 Sivan, eldest first: {string.Join(", ", sivan6.Select(person => person.Key))}.");
        // The grid's months: civil, or Hebrew (13 Kislev 5787 is 23 November 2026; Kislev has 30 days that year).
        DateTime kislevStart = CalendarGrid.MonthStart(Day("2026-11-23"), true);
        Check(kislevStart == Day("2026-11-11") && CalendarGrid.Length(kislevStart, true) == 30 &&
              CalendarGrid.Next(kislevStart, true) == Day("2026-12-11") && CalendarGrid.Previous(kislevStart, true) == Day("2026-10-12") &&
              CalendarGrid.InMonth(Day("2026-12-10"), kislevStart, true) && !CalendarGrid.InMonth(Day("2026-12-11"), kislevStart, true) &&
              CalendarGrid.SameDay(Day("2026-11-23"), Day("2026-12-11"), true) == Day("2026-12-23"),
            $"Hebrew months of the grid: Kislev from {kislevStart:d}.");
        // Five weeks at most: August 2026 starts on a Saturday and has 31 days, so the 30th and 31st take the first week's Sunday and Monday.
        var august = CalendarGrid.Days(Day("2026-08-01"), false);
        Check(august.Length == 35 && august[0] == Day("2026-08-30") && august[1] == Day("2026-08-31") && august[6] == Day("2026-08-01") &&
              august[34] == Day("2026-08-29") && august.Count(day => day.Month == 8) == 31 && august.Distinct().Count() == 35,
            $"A month of six weeks fits in five: {string.Join(" ", august.Select(day => day.Day))}.");
        var november = CalendarGrid.Days(Day("2026-11-01"), false);
        Check(november[0] == Day("2026-11-01") && november[34] == Day("2026-12-05"), "A month of five weeks starts on its own first week.");
        Check(CalendarGrid.Days(kislevStart, true).Count(day => CalendarGrid.InMonth(day, kislevStart, true)) == 30, "A Hebrew month shows all its days in five weeks.");
        Check(CalendarGrid.MonthStart(Day("2026-11-23"), false) == Day("2026-11-01") && CalendarGrid.Next(Day("2026-01-01"), false) == Day("2026-02-01") &&
              CalendarGrid.SameDay(Day("2026-01-31"), Day("2026-02-01"), false) == Day("2026-02-28") && CalendarGrid.Previous(Day("2026-01-01"), false) == Day("2025-12-01"),
            "Civil months of the grid, a shorter month ending on its last day.");
        var outside = DayDetailsBuilder.Cell(Day("2026-03-30"), month, Day("2026-04-02"), false, "English", null, text);
        Check(outside.OutsideMonth && outside.GregorianNumber == "30", "A day of another month is marked.");
        // 20 Tevet is the Rambam's and the Abir Yaakov's: with both hidden the day has no candle.
        var rambam = DayDetailsBuilder.Cell(Day("2026-01-09"), Day("2026-01-01"), Day("2026-01-01"), false, "English", null, text);
        Check(rambam.HasYahrzeit && rambam.Tooltip.Contains("Rambam") && !DayDetailsBuilder.Cell(Day("2026-01-09"), Day("2026-01-01"), Day("2026-01-01"), false, "English", new[] { "rambam", "abir_yaakov" }, text).HasYahrzeit,
            "A day with a yahrzeit is marked, unless it is hidden.");
        Check(DayDetailsBuilder.HebrewMonths(Day("2026-11-01"), "English") == "Heshvan – Kislev 5787" && DayDetailsBuilder.HebrewMonths(Day("2026-09-01"), "English") == "Elul 5786 – Tishri 5787" &&
            DayDetailsBuilder.HebrewMonths(Day("2026-03-01"), "русский") == "адар – нисан 5786", "The Hebrew months of a Gregorian month, with the turn of the year.");
        // All the cells of 42 days of a year: none fails, each has its numbers.
        int cells = 0;
        for (DateTime day = Day("2026-01-01"); day < Day("2027-03-01"); day = day.AddDays(1))
        {
            var cell = DayDetailsBuilder.Cell(day, new DateTime(day.Year, day.Month, 1), Day("2026-06-01"), day.Day % 2 == 0, "English", null, text);
            Check(cell.HebrewNumber.Length == 2 && cell.GregorianNumber == day.Day.ToString(), $"{day:yyyy-MM-dd}: the numbers of the cell.");
            cells++;
        }
        Console.WriteLine($"PASS: the cells of the month show both dates, the festival or portion, the day of rest and the yahrzeit, mark other months, and {cells} days in a row build without a failure.");
    }

    private static void CheckTree()
    {
        var root = YahrzeitNode.Build("English", "All", null);
        Check(root.IsChecked == true && root.Children.Count == Yahrzeits.Categories.Count && root.Children.All(group => group.IsChecked == true) &&
            root.Children.Sum(group => group.Children.Count) == Yahrzeits.All.Count && !root.UncheckedKeys().Any(), "Everything is checked when nothing is hidden.");
        int r = Yahrzeits.Categories.ToList().IndexOf("Rishonim");
        int rashi = root.Children[r].Children.FindIndex(person => person.Key == "rashi");
        int other = rashi == 0 ? 1 : 0;
        Check(root.Children[r].Text == "Rishonim" && rashi >= 0 && root.Children[r].Children[rashi].Text == "Rashi — 29 Tamuz", "A figure is shown with its name and day.");

        var partial = YahrzeitNode.Build("English", "All", new[] { "rashi" });
        var rishonim = partial.Children[r];
        Check(partial.IsChecked == null && rishonim.IsChecked == null && rishonim.Children[rashi].IsChecked == false && rishonim.Children[other].IsChecked == true && partial.Children[0].IsChecked == true &&
            partial.UncheckedKeys().SequenceEqual(new[] { "rashi" }), "Some hidden: the groups above them are partly checked, the others fully.");

        // A click on a figure, on a group, on everything; each announces the change once at the root, and the groups follow.
        int changes = 0;
        root.FigureChanged += (_, _) => changes++;
        root.Children[r].Children[rashi].IsChecked = false;
        Check(changes == 1 && root.Children[r].IsChecked == null && root.IsChecked == null && root.UncheckedKeys().SequenceEqual(new[] { "rashi" }), "Unchecking a figure.");
        root.Children[r].IsChecked = false;
        Check(changes == 2 && root.Children[r].Children.All(person => person.IsChecked == false) && root.UncheckedKeys().Count() == root.Children[r].Children.Count && root.IsChecked == null, "Unchecking a group unchecks its figures.");
        root.Children[r].IsChecked = null;
        Check(root.Children[r].IsChecked == false, "A group is never left in between by a click.");
        root.Children[r].Children[other].IsChecked = true;
        Check(root.Children[r].IsChecked == null && root.UncheckedKeys().Count() == root.Children[r].Children.Count - 1, "Checking one figure of an unchecked group leaves the group in between.");
        root.IsChecked = true;
        Check(root.UncheckedKeys().Count() == 0 && root.Children.All(group => group.IsChecked == true && group.Children.All(person => person.IsChecked == true)), "Checking everything checks every figure.");
        root.IsChecked = false;
        Check(root.UncheckedKeys().Count() == Yahrzeits.All.Count && root.IsChecked == false, "Unchecking everything unchecks every figure.");
        root.IsChecked = true;
        root.Children[0].Children[0].IsChecked = true;
        int before = changes;
        root.Children[0].Children[0].IsChecked = true;
        Check(changes == before, "Setting what already is announces nothing.");

        // The tree tells what changes of its nodes to show.
        var announced = new List<string>();
        var leaf = partial.Children[0].Children[0];
        leaf.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        partial.Children[0].IsChecked = false;
        Check(announced.Contains("IsChecked"), "A node announces that it was checked or unchecked.");
        Console.WriteLine("PASS: the tree of yahrzeits: groups follow their figures (all, none, some), a click on a group takes all or none, one change is announced once, the unchecked figures are what is saved.");
    }

    private static void CheckWindows(DirectoryInfo root, Dictionary<string, string> english)
    {
        string folder = Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Calendar", "Window");
        var windows = new[] { "CalendarWindow", "CalendarSettingsWindow" }.Select(name => (Name: name, Xaml: File.ReadAllText(Path.Combine(folder, name + ".xaml")), Code: File.ReadAllText(Path.Combine(folder, name + ".xaml.cs")))).ToArray();
        var used = new HashSet<string>();
        foreach (var (name, xaml, code) in windows)
        {
            foreach (Match handler in Regex.Matches(xaml, @"\b(?:Click|SelectionChanged|KeyDown|PreviewKeyDown)=""([A-Za-z0-9_]+)"""))
                Check(Regex.IsMatch(code, @"void\s+" + handler.Groups[1].Value + @"\s*\("), $"{name}.xaml names the handler {handler.Groups[1].Value}, which {name}.xaml.cs does not have.");
            foreach (Match key in Regex.Matches(xaml, @"\{DynamicResource ((?:calendar_|hebrew_place)[a-z_]*)\}")) used.Add(key.Groups[1].Value);
            foreach (Match key in Regex.Matches(code, @"Text\(""((?:calendar_|hebrew_place)[a-z_]*)""\)")) used.Add(key.Groups[1].Value);
            Check(xaml.Contains($"x:Class=\"UltraWinBar.{name}\"") && Regex.IsMatch(code, @"public partial class " + name + @"\s*:\s*Window"), $"{name}: the class of the window is declared both ways.");
        }
        // Above the month: the civil month at the left, the week's portion in the middle, the Hebrew months at the right; the
        // day's dates are not repeated in the details; the settings are a gear in the title bar, there is no Close button.
        string calendarXaml = windows[0].Xaml, calendarCode = windows[0].Code;
        int civil = calendarXaml.IndexOf("x:Name=\"MonthTitle\"", StringComparison.Ordinal), portion = calendarXaml.IndexOf("x:Name=\"PortionTitle\"", StringComparison.Ordinal),
            hebrew = calendarXaml.IndexOf("x:Name=\"HebrewMonthTitle\"", StringComparison.Ordinal);
        Check(civil > 0 && civil < portion && portion < hebrew && calendarCode.Contains("PortionTitle.Text = details.Portion") &&
              !calendarCode.Contains("Line(details.Heading") && !calendarCode.Contains("Line(details.HebrewDate") && !calendarCode.Contains("Line(details.Portion"),
            "The month row holds the civil month, the portion and the Hebrew months; the details do not repeat the day's dates.");
        // The month row is over the whole width and holds the chosen day's Hebrew date; the place is behind a globe.
        Check(calendarCode.Contains("MonthTitle.Text = chosen.ToString(\"d MMMM yyyy\"") && calendarCode.Contains("HebrewMonthTitle.Text = details.HebrewDate") &&
              calendarXaml.IndexOf("Grid.ColumnSpan=\"2\"", StringComparison.Ordinal) < civil &&
              calendarCode.Contains("TimesHeading(details.Place)") && calendarCode.Contains("\"\\uE774\"") && !calendarCode.Contains("Line(details.Place"),
            "The month row spans the window with the chosen day's civil and Hebrew dates, and the place of the times is in the globe's popup.");
        Check(calendarXaml.Contains("Content=\"&#xE713;\"") && !calendarXaml.Contains("Content=\"{DynamicResource calendar_close}\"") && !calendarXaml.Contains("IsCancel=\"True\""),
            "The calendar's settings are a gear in the title bar and it has no Close button below.");
        // A click on the date closes only a calendar in sight; one under other windows is brought up over them.
        Check(calendarCode.Contains("&& !IsCovered(handle))") && calendarCode.Contains("shown.Topmost = true;") && calendarCode.Contains("shown.Topmost = false;"),
            "A calendar under other windows is raised by a click on the date, not closed.");
        // A double click on the Hebrew date is a click: no system date and time settings, and its second click does not close.
        string clockCode = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml.cs"));
        int doubleClick = clockCode.IndexOf("private void Clock_OnMouseDoubleClick", StringComparison.Ordinal);
        Check(doubleClick > 0 && clockCode.IndexOf("IsOverHebrewDate()", doubleClick, StringComparison.Ordinal) > 0 &&
              clockCode.IndexOf("IsOverHebrewDate()", doubleClick, StringComparison.Ordinal) < clockCode.IndexOf("timedate.cpl", doubleClick, StringComparison.Ordinal) &&
              clockCode.Contains("CalendarWindow.ShowSingle(this, toggle: _pressClickCount < 2)") && calendarCode.Contains("clock != null && toggle && here"),
            "A double click on the Hebrew date shows the calendar like a click and never opens the system's date and time settings.");
        // The calendar is made ahead (hover over the clock, idle after the start); a click takes the prepared one.
        Check(calendarCode.Contains("public static void Prepare()") && calendarCode.Contains("shown = prepared ?? new CalendarWindow();") &&
              calendarCode.Contains("prepared.madeOn != DateTime.Today") && clockCode.Contains("MouseEnter += (_, _) => PrepareCalendar();") &&
              clockCode.Contains("new Action(CalendarWindow.Prepare)"),
            "The calendar is prepared before the click and a stale one (made on another day) is not shown.");
        // The place: a popup above the globe, right-aligned to it; shown on hover, hidden on leave, pinned and unpinned by clicks.
        Check(calendarCode.Contains("PlacementMode.Custom") && calendarCode.Contains("new Point(targetSize.Width - popupSize.Width, -popupSize.Height - 4)") &&
              calendarCode.Contains("globe.MouseEnter += (_, _) => popup.IsOpen = true;") && calendarCode.Contains("if (!pinned) popup.IsOpen = false;") &&
              calendarCode.Contains("popup.IsOpen = pinned;") && calendarCode.Contains("Deactivated -= away;"),
            "The place's popup stands over the globe inside the calendar, follows the pointer, and a second click hides it.");
        // The cells: the portion as an accent badge, the special days as blocks by kind, the civil date muted.
        // The candle (and the Omer's tree of the sefirot) are in a dictionary shared with the clock.
        string icons = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Calendar", "CalendarIcons.xaml"));
        Check(calendarXaml.Contains("x:Name=\"PortionBadge\"") && calendarXaml.Contains("ItemsSource=\"{Binding Events}\"") &&
              calendarXaml.Contains("<DataTrigger Binding=\"{Binding Kind}\" Value=\"YomTov\">") && calendarXaml.Contains("x:Name=\"YahrzeitCandle\"") && icons.Contains("<RadialGradientBrush"),
            "The cells show the portion, the special days and the yahrzeits as blocks, not plain text.");
        Check(icons.Contains("x:Key=\"YahrzeitCandleIcon\"") && icons.Contains("x:Shared=\"False\"") && calendarXaml.Contains("CalendarIcons.xaml") && calendarCode.Contains("FindResource(\"YahrzeitCandleIcon\")"),
            "Each yahrzeit of the day is listed after a memorial candle.");
        // The counting of the Omer: 16 Nisan is day 1, Lag BaOmer day 33, 5 Sivan day 49; Pesach's first day and Shavuot are
        // outside it. The calendar shows it as a card; the clock puts the tree of the sefirot with the day, and a candle on a yahrzeit.
        Check(JewishCalendar.OmerDay(Day("2026-04-02")) == 0 && JewishCalendar.OmerDay(Day("2026-04-03")) == 1 && JewishCalendar.OmerDay(Day("2026-05-05")) == 33 &&
              JewishCalendar.OmerDay(Day("2026-05-21")) == 49 && JewishCalendar.OmerDay(Day("2026-05-22")) == 0 && JewishCalendar.OmerDay(Day("2026-11-23")) == 0,
            "The days of the Omer run from 16 Nisan (1) to 5 Sivan (49).");
        // In a leap year (5787: Adar I and Adar II) the count still starts on 16 Nisan: 23 April 2027 to 10 June 2027.
        Check(JewishCalendar.OmerDay(Day("2027-04-22")) == 0 && JewishCalendar.OmerDay(Day("2027-04-23")) == 1 && JewishCalendar.OmerDay(Day("2027-05-07")) == 15 &&
              JewishCalendar.OmerDay(Day("2027-05-08")) == 16 && JewishCalendar.OmerDay(Day("2027-06-10")) == 49 && JewishCalendar.OmerDay(Day("2027-06-11")) == 0,
            "In a leap year the Omer runs from 16 Nisan to 5 Sivan too, with 30 Nisan day 15 and 1 Iyar day 16.");
        // Every day of the count once: 49 days in a row, numbered 1 to 49, and 49 different sefirot.
        var omerDays = Enumerable.Range(0, 60).Select(offset => JewishCalendar.OmerDay(Day("2026-03-30").AddDays(offset))).Where(day => day > 0).ToList();
        Check(omerDays.SequenceEqual(Enumerable.Range(1, 49)) &&
              Enumerable.Range(1, 49).Select(day => JewishCalendar.OmerSefira(day, "English")).Distinct().Count() == 49 &&
              JewishCalendar.OmerSefira(7, "English") == "Malchut she'b'Chesed" && JewishCalendar.OmerSefira(8, "English") == "Chesed she'b'Gevurah" &&
              JewishCalendar.OmerSefira(2, "עברית") == "גבורה שבחסד" && JewishCalendar.OmerSefira(50, "English") == "",
            "The count has each day once and each pairing of the sefirot once.");
        // The marks by the clock's weekday: a candle for a yahrzeit that is shown, the Omer's day; after sunset the next day's.
        var lag = DayDetailsBuilder.MarksOf(Day("2026-05-05"), new List<string>());
        var lagHidden = DayDetailsBuilder.MarksOf(Day("2026-05-05"), Yahrzeits.On(Day("2026-05-05")).Select(person => person.Key).ToList());
        // 11 Kislev has no yahrzeit (14 Kislev, the earlier choice, has Reuven's).
        var plain = DayDetailsBuilder.MarksOf(Day("2026-11-21"), null);
        Check(lag.Yahrzeit && lag.Omer == 33 && !lagHidden.Yahrzeit && lagHidden.Omer == 33 && !plain.Yahrzeit && plain.Omer == 0,
            "The clock's marks: a candle unless every yahrzeit of the day is hidden, the Omer's day apart from it.");
        // Jerusalem, 2 April 2026 (15 Nisan): by day no count yet; after its sunset the Hebrew day is 16 Nisan, day 1.
        HebrewDayNow noon = HebrewDayClock.Now(new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc), Jerusalem);
        HebrewDayNow evening = HebrewDayClock.Now(new DateTime(2026, 4, 2, 18, 0, 0, DateTimeKind.Utc), Jerusalem);
        Check(DayDetailsBuilder.MarksOf(noon.Day, null).Omer == 0 && evening.AfterSunset && DayDetailsBuilder.MarksOf(evening.Day, null).Omer == 1,
            $"After sunset the clock shows the count of the night that has begun: {noon.Day:d} / {evening.Day:d}.");
        // The sefira of the day within the week's; the cell carries the day and its line; the place says Israel or the diaspora.
        Check(JewishCalendar.OmerSefira(1, "English") == "Chesed she'b'Chesed" && JewishCalendar.OmerSefira(2, "English") == "Gevurah she'b'Chesed" &&
              JewishCalendar.OmerSefira(33, "English") == "Hod she'b'Hod" && JewishCalendar.OmerSefira(49, "русский") == "Малхут ше-бе-Малхут" &&
              JewishCalendar.OmerSefira(0, "English") == "",
            "The sefirot of the Omer: the day's within the week's.");
        var englishText = EnglishStrings(root);
        Func<string, string> Text = key => englishText[key];
        var lagBaomer = DayDetailsBuilder.Cell(Day("2026-05-05"), Day("2026-05-01"), Day("2026-05-05"), false, "English", new List<string>(), Text);
        Check(lagBaomer.HasOmer && lagBaomer.Omer == 33 && lagBaomer.OmerText.EndsWith("Hod she'b'Hod") &&
              !DayDetailsBuilder.Cell(Day("2026-11-23"), Day("2026-11-01"), Day("2026-11-23"), false, "English", new List<string>(), Text).HasOmer &&
              calendarXaml.Contains("Visibility=\"{Binding HasOmer, Converter={StaticResource OmerVisibility}}\"") && calendarXaml.Contains("<Border.ToolTip>"),
            $"A cell of the Omer has its day, the tree of the sefirot by the candle's place, and the sefira in its tooltip: {lagBaomer.OmerText}.");
        Check(calendarCode.Contains("JewishCalendar.OmerDay(chosen)") && calendarCode.Contains("Text(\"calendar_omer\")") && clockCode.Contains("DayDetailsBuilder.MarksOf(hebrewDay.Day, Settings.Instance.HiddenYahrzeits)") && icons.Contains("x:Key=\"OmerIcon\""),
            "The calendar has an Omer card; the clock shows the Omer's day with the tree of the sefirot and a candle on a yahrzeit.");
        // The days of the week over the grid: 1 (Sunday) to 6 (Friday) and Shabbat, no day names of a language.
        Check(CalendarWindow.WeekdayHeaders("Shabbat").SequenceEqual(new[] { "1", "2", "3", "4", "5", "6", "Shabbat" }) &&
              calendarCode.Contains("WeekdayHeaders(Text(\"calendar_shabbat\"))") && !calendarCode.Contains("AbbreviatedDayNames"),
            "The days of the week are numbered 1 to 6, then Shabbat.");
        // The grid switches between civil and Hebrew months; the arrows, PageUp/PageDown and the keys follow the kind shown.
        Check(calendarXaml.Contains("Click=\"GridSwitch_Click\"") && calendarCode.Contains("Settings.Instance.CalendarHebrewGrid = !Settings.Instance.CalendarHebrewGrid;") &&
              calendarCode.Contains("CalendarGrid.InMonth(cell.Date, month, hebrew)") && !calendarCode.Contains("AddMonths("),
            "The calendar's grid can show civil or Hebrew months, and moves by the kind shown.");
        // A name of the day: on hover, copy it, or look it up with Google in the system's browser (a one-color Chrome mark).
        Check(CalendarWindow.SearchAddress("Rabbi Akiva Eiger") == "https://www.google.com/search?q=Rabbi%20Akiva%20Eiger" &&
              CalendarWindow.SearchAddress("Vilna Gaon (Gra)") == "https://www.google.com/search?q=Vilna%20Gaon" &&
              CalendarWindow.SearchAddress("רש״י").StartsWith("https://www.google.com/search?q=%D7%A8") &&
              calendarCode.Contains("Clipboard.SetText(name)") && calendarCode.Contains("UseShellExecute = true") &&
              calendarXaml.Contains("x:Key=\"ChromeIcon\"") && calendarCode.Contains("row.MouseEnter += (_, _) => actions.Visibility = Visibility.Visible;"),
            "Each yahrzeit name can be copied or looked up with Google from buttons shown on hover.");
        // After sunset the ending day (lower right half) and the begun one (upper left half) are shaded: a parallelogram.
        Check(calendarXaml.Contains("x:Name=\"EveningEndShade\"") && calendarXaml.Contains("Points=\"1,0 1,1 0,1\"") &&
              calendarXaml.Contains("x:Name=\"EveningBeginShade\"") && calendarXaml.Contains("Points=\"0,0 1,0 0,1\"") &&
              calendarCode.Contains("EveningEnds = now.Civil == cell.Date") && calendarCode.Contains("EveningBegins = now.AfterSunset && now.Day == cell.Date") &&
              calendarCode.Contains("shown.RebuildIfEveningChanged()"),
            "From sunset to midnight the two days share a shade, and a calendar shown again follows the sunset.");
        // The calendar fills the work area of the clock's screen and is neither resized nor maximized.
        Check(!calendarXaml.Contains("SizeToContent") && calendarXaml.Contains("ResizeMode=\"NoResize\"") && calendarXaml.Contains("ResizeBorderThickness=\"0\"") &&
              calendarCode.Contains("shown.FillWorkArea(clock);") && calendarCode.Contains("Width = area.Width;") && calendarCode.Contains("Height = area.Height;") &&
              calendarCode.Contains("CalendarGrid.Days(month, hebrew)") && !calendarCode.Contains("Enumerable.Range(0, 42)"),
            "The calendar fills the work area, cannot be resized or maximized, and shows five weeks.");
        // The weeks are as high as their fullest day, within the height there is; the details are cards.
        // The weeks are all as high as the fullest; only when the month does not fit do the weeks with less in them give way.
        var roomy = WeekRowsPanel.Fit(new double[] { 70, 140, 70 }, 500);
        var tight = WeekRowsPanel.Fit(new double[] { 70, 140, 70 }, 400);
        var squeezed = WeekRowsPanel.Fit(new double[] { 70, 200, 130 }, 300);
        Check(roomy.All(row => Math.Abs(row - 500.0 / 3) < 0.01) &&
              Math.Abs(tight[0] - 130) < 0.01 && Math.Abs(tight[1] - 140) < 0.01 && Math.Abs(tight[2] - 130) < 0.01 &&
              Math.Abs(squeezed[0] - 70) < 0.01 && Math.Abs(squeezed[1] - 150) < 0.01 && Math.Abs(squeezed[2] - 97.5) < 0.01,
            $"Weeks: equal when there is room ({string.Join(",", roomy)}), the emptier ones give way first ({string.Join(",", tight)}), then all, never under the lowest ({string.Join(",", squeezed)}).");
        Check(calendarXaml.Contains("<calendar:WeekRowsPanel />") && !calendarXaml.Contains("<UniformGrid Columns=\"7\"\n") &&
              calendarCode.Contains("private Panel Card(FrameworkElement heading)") && calendarCode.Contains("card.Children.Add(Chip(special))"),
            "The month's weeks are sized by their days and the details are drawn as cards.");
        Check(calendarXaml.Contains("<DataTrigger Binding=\"{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType={x:Type ListBoxItem}}}\"") &&
              calendarXaml.Contains("Value=\"{DynamicResource ModernGlassHover}\""),
            "The day under the pointer is lit clearly.");
        int cell = calendarXaml.IndexOf("<Border x:Name=\"Cell\"", StringComparison.Ordinal);
        Check(cell > 0 && calendarXaml.IndexOf("Background=\"{DynamicResource ModernGlass}\"", cell, StringComparison.Ordinal) - cell < 400 &&
              calendarXaml.IndexOf("BorderBrush=\"{DynamicResource ModernGlassBorder}\"", cell, StringComparison.Ordinal) - cell < 400,
            "Every day of the grid is a card with its own fill and border.");
        Check(calendarCode.Contains("if (time.Rest)") && calendarCode.Contains("card.Children.Add(RestLine(details.RestStarts, null));") &&
              calendarCode.Contains("private FrameworkElement RestLine(string text, string time)"),
            "The candles, the havdalah and the start and end of a day of rest are highlighted lines with candles.");
        // Window.Resources holds one dictionary: a resource after it set Resources twice and the window failed to load.
        int resourcesEnd = calendarXaml.IndexOf("</Window.Resources>", StringComparison.Ordinal);
        int dictionaryEnd = calendarXaml.LastIndexOf("</ResourceDictionary>", resourcesEnd, StringComparison.Ordinal);
        Check(dictionaryEnd > 0 && calendarXaml.Substring(dictionaryEnd + "</ResourceDictionary>".Length, resourcesEnd - dictionaryEnd - "</ResourceDictionary>".Length).Trim().Length == 0,
            "Every resource of the calendar window is inside its one ResourceDictionary.");
        Check(File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml")).Contains("x:Name=\"HebrewDateHost\" Grid.Row=\"2\" Background=\"Transparent\" Cursor=\"Hand\""),
            "The Hebrew date of the clock shows the hand cursor: it is a link to the calendar.");
        foreach (string key in new[] { "calendar_day_of", "calendar_portion", "calendar_festival_reading", "calendar_molad", "calendar_place", "calendar_candles_after_dark", "calendar_havdalah", "calendar_rest_begins", "calendar_rest_ends" }) used.Add(key);
        var missing = used.Where(key => !english.ContainsKey(key)).ToArray();
        Check(missing.Length == 0, $"English lacks strings the calendar uses: {string.Join(", ", missing)}.");
        string languages = Path.Combine(root.FullName, "UltraWinBar", "Assets", "Languages");
        var calendarKeys = english.Keys.Where(key => key.StartsWith("calendar_")).ToArray();
        Check(calendarKeys.Length >= 24, $"The calendar has its strings: {calendarKeys.Length}.");
        foreach (string language in new[] { Path.Combine("Group06", "русский.xaml"), Path.Combine("Group05", "беларуская.xaml") })
        {
            var strings = Strings(Path.Combine(languages, language));
            Check(calendarKeys.All(strings.ContainsKey), $"{language} lacks: {string.Join(", ", calendarKeys.Where(key => !strings.ContainsKey(key)))}.");
            // The same numbers of placeholders in each text as in English, so a translation cannot break a format.
            foreach (string key in calendarKeys)
                Check(Regex.Matches(strings[key], @"\{\d\}").Count == Regex.Matches(english[key], @"\{\d\}").Count, $"{language}: {key} has other placeholders than the English text.");
        }

        // The clock: a click on the Hebrew date opens the calendar; the setting of the yahrzeits is kept as given.
        string clock = File.ReadAllText(Path.Combine(root.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml.cs"));
        Check(clock.Contains("CalendarWindow.ShowSingle(") && clock.Contains("\"HebrewDateHost\"") && clock.IndexOf("CalendarWindow.ShowSingle(", StringComparison.Ordinal) < clock.IndexOf("ClockClickOption.DoNothing", StringComparison.Ordinal),
            "A click on the Hebrew date opens the calendar before the clock's own action is considered.");
        var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
        var property = settingsType.GetProperty("HiddenYahrzeits") ?? throw new Exception("Settings.HiddenYahrzeits is missing.");
        var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        object settings = Activator.CreateInstance(settingsType);
        Check(((List<string>)property.GetValue(settings)).Count == 0, "Nobody is hidden by default.");
        property.SetValue(settings, new List<string> { "rashi", "ramak" });
        object back = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, settingsType, options), settingsType, options);
        Check(((List<string>)property.GetValue(back)).SequenceEqual(new[] { "rashi", "ramak" }), "The hidden figures survive the settings file.");
        property.SetValue(settings, null);
        Check(((List<string>)property.GetValue(settings)).Count == 0, "No list is an empty list.");
        Console.WriteLine("PASS: both calendar windows name only handlers they have, every string they use is in English, Russian and Belarusian with the same placeholders, a click on the Hebrew date opens the calendar, and the hidden yahrzeits are kept in the settings.");
    }
}
