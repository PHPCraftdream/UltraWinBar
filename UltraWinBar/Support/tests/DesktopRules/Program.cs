using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

var repositoryRoot = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
while (repositoryRoot != null && !System.IO.File.Exists(System.IO.Path.Combine(repositoryRoot.FullName, "README.md")))
    repositoryRoot = repositoryRoot.Parent;
if (repositoryRoot == null) throw new Exception("Repository root not found for structure checks.");
var generatedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "artifacts" };
var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".xaml", ".json", ".iss", ".ps1", ".bat", ".pubxml", ".hlsl", ".props", ".csproj", ".sln", ".md", ".yml" };
void CheckDirectory(System.IO.DirectoryInfo folder)
{
    var entries = folder.EnumerateFileSystemInfos().Where(entry =>
        !generatedFolders.Contains(entry.Name) && (entry.Attributes & System.IO.FileAttributes.ReparsePoint) == 0).ToArray();
    if (entries.Length > 7) throw new Exception($"More than seven source entries: {folder.FullName} ({entries.Length})");
    foreach (var entry in entries)
    {
        if (entry is System.IO.DirectoryInfo child) CheckDirectory(child);
        else if (entry is System.IO.FileInfo file && sourceExtensions.Contains(file.Extension) &&
                 System.IO.File.ReadLines(file.FullName).Take(1001).Count() > 1000)
            throw new Exception($"Source file exceeds 1000 lines: {file.FullName}");
    }
}
CheckDirectory(repositoryRoot);
Console.WriteLine("PASS: every source folder has at most seven entries; every source file has at most 1000 lines.");

var desktopA = Guid.Parse("00000000-0000-0000-0000-000000000001");
var widthConverter = new UltraWinBar.Converters.TaskButtonWidthConverter();
foreach (bool launcher in new[] { false, true })
foreach (var orientation in new[] { System.Windows.Controls.Orientation.Horizontal, System.Windows.Controls.Orientation.Vertical })
{
    double expectedWidth = launcher && orientation == System.Windows.Controls.Orientation.Horizontal ? 34 : 132;
    object actualWidth = widthConverter.Convert(new object[] { 0, 132d, 0, 1, launcher, orientation },
        typeof(double), null, System.Globalization.CultureInfo.InvariantCulture);
    if (!expectedWidth.Equals(actualWidth)) throw new Exception($"Pinned width mismatch: {launcher}/{orientation}");
}
Console.WriteLine("PASS: vertical pinned launchers fill the panel; horizontal launchers remain compact.");
var screen = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
var edges = new[] { AppBarEdge.Left, AppBarEdge.Top, AppBarEdge.Right, AppBarEdge.Bottom };
void VerifyNonOverlapping(AppBarEdge[] order, ManagedShell.Interop.NativeMethods.Rect bounds, int thickness)
{
    var layout = PanelLayout.Calculate(bounds, order, _ => thickness);
    foreach (var (_, panel) in layout.Panels)
    {
        if (panel.Left < bounds.Left || panel.Right > bounds.Right || panel.Top < bounds.Top || panel.Bottom > bounds.Bottom ||
            panel.Width < 0 || panel.Height < 0)
            throw new Exception("Panel exceeds the monitor bounds.");
    }
    for (int i = 0; i < layout.Panels.Count; i++)
    for (int j = i + 1; j < layout.Panels.Count; j++)
    {
        var panel = layout.Panels[i].Bounds;
        var other = layout.Panels[j].Bounds;
        if (Math.Min(panel.Right, other.Right) > Math.Max(panel.Left, other.Left) &&
            Math.Min(panel.Bottom, other.Bottom) > Math.Max(panel.Top, other.Top))
            throw new Exception($"Panels overlap: {layout.Panels[i].Edge} and {layout.Panels[j].Edge}.");
    }
}
void CheckPermutations(AppBarEdge[] chosen, AppBarEdge[] remaining)
{
    VerifyNonOverlapping(chosen, screen, 158);
    VerifyNonOverlapping(chosen, new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 220, Bottom = 180 }, 158);
    foreach (var edge in remaining)
        CheckPermutations(chosen.Append(edge).ToArray(), remaining.Where(candidate => candidate != edge).ToArray());
}
CheckPermutations(Array.Empty<AppBarEdge>(), edges);
var topFirst = PanelLayout.Calculate(screen, new[] { AppBarEdge.Top, AppBarEdge.Left }, _ => 40);
if (topFirst.Panels[0].Bounds.Right != 1920 || topFirst.Panels[1].Bounds.Top != 40)
    throw new Exception("Edge priority was not preserved.");
Console.WriteLine("PASS: panel bounds never intersect for every edge subset/order, including tight monitors.");
if (Array.IndexOf(args, "--themes") >= 0) ThemeChecks.Run();
if (ReleaseEndpoints.Releases != "https://github.com/PHPCraftdream/UltraWinBar/releases" ||
    ReleaseEndpoints.LatestReleaseApi != "https://api.github.com/repos/PHPCraftdream/UltraWinBar/releases/latest")
    throw new Exception("Release endpoints do not target this project.");
if (Array.IndexOf(args, "--desktop-interop") >= 0)
{
    using var desktopActions = new DesktopActions();
    var desktops = DesktopActions.GetDesktops();
    if (desktops.Count == 0) throw new Exception("No virtual desktops enumerated.");
    bool pinned = desktopActions.IsApplicationPinned(ManagedShell.Interop.NativeMethods.GetForegroundWindow());
    Console.WriteLine($"PASS: read-only desktop COM integration; desktops={desktops.Count}, foregroundAppPinned={pinned}.");
}
var desktopB = Guid.Parse("00000000-0000-0000-0000-000000000002");
var rules = new List<TaskbarAssignment>
{
    new() { Identifier = "browser", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Bottom },
    new() { Identifier = "browser", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Left, DesktopId = desktopA },
    new() { Identifier = "browser", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Right, DesktopId = desktopB },
    new() { Identifier = "window", Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Top, DesktopId = desktopA },
};
void Check(Guid desktop, string window, AppBarEdge expected)
{
    var actual = TaskAssignmentManager.ResolveEdge(rules, desktop, window, "browser");
    if (actual != expected) throw new Exception($"{desktop}/{window}: expected {expected}, got {actual}");
}
Check(desktopA, "other", AppBarEdge.Left);
Check(desktopB, "window", AppBarEdge.Right);
Check(desktopA, "window", AppBarEdge.Top);
Check(Guid.NewGuid(), "window", AppBarEdge.Bottom);
rules = JsonSerializer.Deserialize<List<TaskbarAssignment>>(JsonSerializer.Serialize(rules));
Check(desktopA, "window", AppBarEdge.Top);
Check(desktopB, "window", AppBarEdge.Right);
rules.RemoveAll(rule => rule.DesktopId == desktopA);
Check(desktopB, "window", AppBarEdge.Right);
Check(desktopA, "window", AppBarEdge.Bottom);
rules = JsonSerializer.Deserialize<List<TaskbarAssignment>>("[{\"Identifier\":\"browser\",\"Edge\":0,\"Mode\":0}]");
Check(desktopA, "window", AppBarEdge.Left);
Console.WriteLine("PASS: desktop isolation, window precedence, JSON restart round-trip, scoped removal, legacy settings.");

var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
var settings = Activator.CreateInstance(settingsType);
var actualDefaults = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(settings, settingsType));
var expectedDefaults = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(AppContext.BaseDirectory, "UltraWinBar.defaults.json"))).AsObject();
foreach (var property in expectedDefaults)
    if (!System.Text.Json.Nodes.JsonNode.DeepEquals(property.Value, actualDefaults[property.Key]))
        throw new Exception($"Default mismatch: {property.Key}: expected {property.Value}, actual {actualDefaults[property.Key]}");
foreach (var name in new[] { "PinnedApplications", "TaskOrder", "TaskbarAssignments", "AllDesktopApplications" })
    if (actualDefaults[name].AsArray().Count != 0) throw new Exception($"Personal data in defaults: {name}");
Console.WriteLine("PASS: current general settings are defaults; no personal pins, desktop IDs or assignments embedded.");

var pinSettings = JsonSerializer.Deserialize("{\"AllDesktopApplications\":[\"test.app\",\"test.app\",\"\"],\"DesktopPinPreferencesInitialized\":true}", settingsType);
var pinState = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(pinSettings, settingsType));
if (pinState["AllDesktopApplications"].AsArray().Count != 1 ||
    pinState["AllDesktopApplications"][0].GetValue<string>() != "test.app" ||
    !pinState["DesktopPinPreferencesInitialized"].GetValue<bool>())
    throw new Exception("Desktop pin preference round-trip failed.");
settingsType.GetProperty("AllDesktopApplications").SetValue(pinSettings, new List<string>());
var unpinned = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(pinSettings, settingsType));
if (unpinned["AllDesktopApplications"].AsArray().Count != 0)
    throw new Exception("Removed desktop pin remains persisted.");
Console.WriteLine("PASS: persistent desktop pins round-trip, deduplication, removal, and empty defaults.");

var windowKeys = new[] { TaskOrderIdentifier.CreateKey(100, 200, 300),
    TaskOrderIdentifier.CreateKey(100, 200, 301), TaskOrderIdentifier.CreateKey(100, 200, 302) };
var desired = new[] { windowKeys[2], windowKeys[0], windowKeys[1] };
foreach (var discovery in new[] { windowKeys, windowKeys.Reverse().ToArray(), new[] { windowKeys[1], windowKeys[2], windowKeys[0] } })
{
    var restored = discovery.OrderBy(key => Array.IndexOf(desired, key));
    if (!restored.SequenceEqual(desired)) throw new Exception("Discovery order changed restored order.");
}
if (TaskOrderIdentifier.CreateKey(100, 201, 300) == windowKeys[0])
    throw new Exception("Reused process/window handle shares an order identity.");
Console.WriteLine("PASS: window identities restore identical order across discovery permutations and distinguish process lifetimes.");

foreach (var example in new[] {
    (new DateTime(2016, 12, 1), "01 Kislev 5777"),
    (new DateTime(2024, 2, 10), "01 Adar I 5784"),
    (new DateTime(2024, 3, 11), "01 Adar II 5784"),
    (new DateTime(2023, 2, 22), "01 Adar 5783"),
    (new DateTime(2023, 9, 16), "01 Tishri 5784") })
    if (HebrewClockFormatter.FormatDate(example.Item1) != example.Item2)
        throw new Exception($"Hebrew date mismatch for {example.Item1:yyyy-MM-dd}");
var dayNames = new[] { "Day-1", "Day-2", "Day-3", "Day-4", "Day-5", "Day-6", "Shabbat" };
for (int i = 0; i < 7; i++)
    if (HebrewClockFormatter.DayName((DayOfWeek)i) != dayNames[i]) throw new Exception("Wrong weekday label.");
if (HebrewClockFormatter.FormatDate(DateTime.MinValue) != "" || actualDefaults["ShowHebrewDate"].GetValue<bool>())
    throw new Exception("Hebrew clock boundary/default mismatch.");
Console.WriteLine("PASS: Hebrew dates, ordinary/leap Adar, new year, weekday labels, and disabled default.");
if (HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "русский") != "01 кислев 5777" ||
    HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "עברית") != "01 כסלו 5777")
    throw new Exception("Localized Hebrew month mismatch.");
var hebrewCalendar = new System.Globalization.HebrewCalendar();
foreach (string language in HebrewClockFormatter.SupportedLanguages)
foreach (int year in new[] { 5783, 5784 })
for (int month = 1; month <= hebrewCalendar.GetMonthsInYear(year); month++)
{
    var date = hebrewCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
    if (!HebrewClockFormatter.FormatDate(date, language).EndsWith(year.ToString()))
        throw new Exception($"Missing Hebrew month translation: {language}/{year}/{month}");
}
Console.WriteLine($"PASS: CLDR month names for {HebrewClockFormatter.SupportedLanguages.Count} UI languages in ordinary and leap years.");
if (HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "беларуская") != "01 кіслеў 5777")
    throw new Exception("Belarusian month is not localized.");
foreach (int year in new[] { 5783, 5784 })
for (int month = 1; month <= hebrewCalendar.GetMonthsInYear(year); month++)
{
    string text = HebrewClockFormatter.FormatDate(hebrewCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0), "беларуская");
    text = System.Text.RegularExpressions.Regex.Replace(text, @"\bI{1,2}\b", "");
    if (System.Text.RegularExpressions.Regex.IsMatch(text, "[A-Za-z]")) throw new Exception("Latin Belarusian month name.");
}
var englishXml = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "English.xaml"));
var belarusianXml = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "Belarusian.xaml"));
System.Xml.Linq.XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
var requiredKeys = englishXml.Root.Elements().Where(e => e.Name.LocalName is "String" or "Array")
    .Select(e => (string)e.Attribute(xamlNamespace + "Key"));
var translatedKeys = belarusianXml.Root.Elements().Select(e => (string)e.Attribute(xamlNamespace + "Key")).ToArray();
if (requiredKeys.Except(translatedKeys).Any()) throw new Exception("Missing Belarusian translations: " + string.Join(",", requiredKeys.Except(translatedKeys)));
foreach (var text in belarusianXml.Descendants().Where(e => e.Name.LocalName == "String").Select(e => e.Value))
{
    string words = System.Text.RegularExpressions.Regex.Replace(text, "UltraWinBar|Themes|Aero|GitHub|Windows|Win|Ctrl|IME", "");
    if (System.Text.RegularExpressions.Regex.IsMatch(words, "[A-Za-zÀ-ʯ]")) throw new Exception("Latin Belarusian UI text: " + text);
}
Console.WriteLine("PASS: complete Belarusian UI translations and Cyrillic Hebrew month names.");

var area = new System.Windows.Rect(158, 41, 1604, 998);
var bar = new ManagedShell.Interop.NativeMethods.Rect { Left = 1762, Top = 41, Right = 1920, Bottom = 1080 };
var menu = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 509, Bottom = 588 };
foreach (AppBarEdge edge in Enum.GetValues<AppBarEdge>())
foreach (bool rtl in new[] { false, true })
{
    var target = StartMenuPlacement.GetTarget(menu, bar, area, edge, rtl);
    if (!area.Contains(new System.Windows.Rect(target, new System.Windows.Size(menu.Width, menu.Height))))
        throw new Exception($"Menu outside work area: {edge}, RTL={rtl}");
}
var rightTarget = StartMenuPlacement.GetTarget(menu, bar, area, AppBarEdge.Right, false);
if (rightTarget.X != bar.Left - menu.Width) throw new Exception("Right menu width ignored");
Console.WriteLine("PASS: Start menu bounds for every edge and text direction; full menu size used.");
