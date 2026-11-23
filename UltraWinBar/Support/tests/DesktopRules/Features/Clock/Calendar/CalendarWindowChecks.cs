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
        Check(shabbat.Times.Count == 12 && shabbat.Times[2].Name == "Sunrise (Netz)" && shabbat.Times[9].Name == "Sunset (Shkiah)" && shabbat.Times[10].Name == "Nightfall (Tzeit HaKochavim)" &&
            shabbat.Times[11].Name == "End of the day of rest (havdalah)" && shabbat.Times.All(time => Regex.IsMatch(time.Time, @"^\d{1,2}:\d\d\s[AP]M$")), $"The times of the day: {string.Join("; ", shabbat.Times)}.");
        Check(shabbat.Times[11].Time == shabbat.Times[10].Time, "Havdalah is at nightfall.");
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
        Check(rambam.Yahrzeits.SequenceEqual(new[] { "Rambam (Maimonides)" }) && DayDetailsBuilder.Describe(Day("2026-01-09"), null, "русский", null, text).Yahrzeits.SequenceEqual(new[] { "Рамбам (Маймонид)" }) &&
            DayDetailsBuilder.Describe(Day("2026-01-09"), null, "English", new[] { "rambam" }, text).Yahrzeits.Count == 0, "The yahrzeit of the Rambam shows, in the language chosen, unless hidden.");

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
        Check(pesach.GregorianNumber == "2" && pesach.HebrewNumber == "15" && pesach.Marker == "Pesach, day 1 of 8" && !pesach.OutsideMonth && pesach.IsToday && pesach.IsRest && !pesach.HasYahrzeit,
            $"The cell of the first day of Pesach: {pesach}.");
        Check(pesach.HebrewLabel == "Nisan, 15" && pesach.GregorianLabel == "2, April", $"The labels of the cell: {pesach.HebrewLabel} / {pesach.GregorianLabel}.");
        var kislev = DayDetailsBuilder.Cell(Day("2026-11-23"), Day("2026-11-01"), Day("2026-11-23"), false, "English", null, text);
        Check(kislev.HebrewLabel == "Kislev, 13" && kislev.GregorianLabel == "23, November", $"The labels of the cell: {kislev.HebrewLabel} / {kislev.GregorianLabel}.");
        var russian = DayDetailsBuilder.Cell(Day("2026-11-23"), Day("2026-11-01"), Day("2026-11-23"), false, "русский", null, text);
        Check(russian.GregorianLabel == "23, ноября" && russian.HebrewLabel.EndsWith(", 13"), $"In Russian the civil month is in the genitive: {russian.GregorianLabel} / {russian.HebrewLabel}.");
        Check(pesach.Tooltip.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Length == 3 && pesach.Tooltip.Contains("15 Nisan 5786"), $"The tooltip: {pesach.Tooltip}.");
        var shabbat = DayDetailsBuilder.Cell(Day("2026-04-04"), month, Day("2026-04-02"), false, "English", null, text);
        Check(shabbat.Marker == "Chol HaMoed Pesach, day 3 of 8" && shabbat.IsRest && !shabbat.IsToday, "A Shabbat of Chol HaMoed carries the festival, not a portion.");
        var portion = DayDetailsBuilder.Cell(Day("2026-04-18"), month, Day("2026-04-02"), false, "English", null, text);
        Check(portion.Marker.Length > 0 && portion.IsRest && DayDetailsBuilder.Cell(Day("2026-04-15"), month, Day("2026-04-02"), false, "English", null, text).Marker == "", "A Shabbat carries its portion, an ordinary day nothing.");
        // The blocks of a cell: the portion of a Shabbat (not on a festival's), and the special days with their kinds.
        Check(portion.Portion != null && portion.Events.Count == 0 && shabbat.Portion == null && shabbat.Events.Count == 1 && shabbat.Events[0].Kind == DayKind.CholHamoed &&
              pesach.Events.Count == 1 && pesach.Events[0].Kind == DayKind.YomTov && pesach.Events[0].Name == pesach.Marker,
            $"The blocks of the cells: {portion.Portion} / {shabbat.Events.Count} / {pesach.Events.Count}.");
        // The yahrzeits of a day come eldest first: on 6 Sivan King David (born 2854) before the Baal Shem Tov (5458).
        Check(Yahrzeits.All.All(person => person.Born > 2000 && person.Born < 5800), "Every figure has a year of birth (anno mundi).");
        var sivan6 = Yahrzeits.On(Day("2026-05-22"));
        Check(sivan6.Select(person => person.Key).SequenceEqual(new[] { "david", "baal_shem_tov" }), $"6 Sivan, eldest first: {string.Join(", ", sivan6.Select(person => person.Key))}.");
        var outside = DayDetailsBuilder.Cell(Day("2026-03-30"), month, Day("2026-04-02"), false, "English", null, text);
        Check(outside.OutsideMonth && outside.GregorianNumber == "30", "A day of another month is marked.");
        var rambam = DayDetailsBuilder.Cell(Day("2026-01-09"), Day("2026-01-01"), Day("2026-01-01"), false, "English", null, text);
        Check(rambam.HasYahrzeit && rambam.Tooltip.Contains("Rambam") && !DayDetailsBuilder.Cell(Day("2026-01-09"), Day("2026-01-01"), Day("2026-01-01"), false, "English", new[] { "rambam" }, text).HasYahrzeit,
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
        Check(root.Children[2].Text == "Rishonim" && root.Children[2].Children[0].Text == "Rashi — 29 Tamuz" && root.Children[2].Children[0].Key == "rashi", "A figure is shown with its name and day.");

        var partial = YahrzeitNode.Build("English", "All", new[] { "rashi" });
        var rishonim = partial.Children[2];
        Check(partial.IsChecked == null && rishonim.IsChecked == null && rishonim.Children[0].IsChecked == false && rishonim.Children[1].IsChecked == true && partial.Children[0].IsChecked == true &&
            partial.UncheckedKeys().SequenceEqual(new[] { "rashi" }), "Some hidden: the groups above them are partly checked, the others fully.");

        // A click on a figure, on a group, on everything; each announces the change once at the root, and the groups follow.
        int changes = 0;
        root.FigureChanged += (_, _) => changes++;
        root.Children[2].Children[0].IsChecked = false;
        Check(changes == 1 && root.Children[2].IsChecked == null && root.IsChecked == null && root.UncheckedKeys().SequenceEqual(new[] { "rashi" }), "Unchecking a figure.");
        root.Children[2].IsChecked = false;
        Check(changes == 2 && root.Children[2].Children.All(person => person.IsChecked == false) && root.UncheckedKeys().Count() == root.Children[2].Children.Count && root.IsChecked == null, "Unchecking a group unchecks its figures.");
        root.Children[2].IsChecked = null;
        Check(root.Children[2].IsChecked == false, "A group is never left in between by a click.");
        root.Children[2].Children[1].IsChecked = true;
        Check(root.Children[2].IsChecked == null && root.UncheckedKeys().Count() == root.Children[2].Children.Count - 1, "Checking one figure of an unchecked group leaves the group in between.");
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
              calendarCode.Contains("TimesHeading(details.Place)") && calendarCode.Contains("\"\uE774\"") && !calendarCode.Contains("Line(details.Place"),
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
        Check(calendarXaml.Contains("x:Name=\"PortionBadge\"") && calendarXaml.Contains("ItemsSource=\"{Binding Events}\"") &&
              calendarXaml.Contains("<DataTrigger Binding=\"{Binding Kind}\" Value=\"YomTov\">") && calendarXaml.Contains("x:Name=\"YahrzeitCandle\"") && calendarXaml.Contains("<RadialGradientBrush"),
            "The cells show the portion, the special days and the yahrzeits as blocks, not plain text.");
        Check(calendarXaml.Contains("x:Key=\"YahrzeitCandleIcon\"") && calendarXaml.Contains("x:Shared=\"False\"") && calendarCode.Contains("FindResource(\"YahrzeitCandleIcon\")"),
            "Each yahrzeit of the day is listed after a memorial candle.");
        // The days of the week over the grid: 1 (Sunday) to 6 (Friday) and Shabbat, no day names of a language.
        Check(CalendarWindow.WeekdayHeaders("Shabbat").SequenceEqual(new[] { "1", "2", "3", "4", "5", "6", "Shabbat" }) &&
              calendarCode.Contains("WeekdayHeaders(Text(\"calendar_shabbat\"))") && !calendarCode.Contains("AbbreviatedDayNames"),
            "The days of the week are numbered 1 to 6, then Shabbat.");
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
