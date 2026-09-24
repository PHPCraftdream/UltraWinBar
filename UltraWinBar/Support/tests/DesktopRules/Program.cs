using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

var repositoryRoot = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
while (repositoryRoot != null && !System.IO.File.Exists(System.IO.Path.Combine(repositoryRoot.FullName, "README.md")))
    repositoryRoot = repositoryRoot.Parent;
if (repositoryRoot == null) throw new Exception("Repository root not found for structure checks.");
var taskbarMenus = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(repositoryRoot.FullName,
    "UltraWinBar", "Shell", "Panels", "Taskbar.xaml"));
System.Xml.Linq.XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
var clockGroup = taskbarMenus.Descendants(presentation + "GroupBox")
    .Single(element => (string)element.Attribute(xaml + "Name") == "ClockGroupBox");
var clockMenu = clockGroup.Element(presentation + "GroupBox.ContextMenu");
var appBarMenuProperty = taskbarMenus.Root.Elements()
    .Single(element => element.Name.LocalName == "AppBarWindow.ContextMenu");
var appBarMenu = appBarMenuProperty.Element(presentation + "ContextMenu");
if (appBarMenu == null) throw new Exception("AppBarWindow context menu is missing.");
var systemSettingsCommand = appBarMenu.Descendants(presentation + "StaticResourceExtension")
    .SingleOrDefault(element => (string)element.Attribute("ResourceKey") == "OpenSystemSettingsMenuItem");
var computerManagementCommand = appBarMenu.Descendants(presentation + "StaticResourceExtension")
    .SingleOrDefault(element => (string)element.Attribute("ResourceKey") == "OpenComputerManagementMenuItem");
var drivesMenu = appBarMenu.Descendants(presentation + "MenuItem")
    .SingleOrDefault(element => (string)element.Attribute(xaml + "Name") == "OpenDrivesMenuItem");
if (systemSettingsCommand == null || computerManagementCommand == null || drivesMenu == null ||
    !drivesMenu.Elements(presentation + "MenuItem").Any(element =>
        (string)element.Attribute(xaml + "Name") == "ThisPcMenuItem" &&
        (string)element.Attribute("Header") == "{DynamicResource this_pc}"))
    throw new Exception("AppBarWindow context menu is missing system commands, This PC, or the drives submenu.");
var exitMenuItems = taskbarMenus.Descendants()
    .Where(element => element.Name.LocalName == "StaticResourceExtension" &&
        (string)element.Attribute("ResourceKey") == "ExitMenuItem").ToArray();
if (clockMenu == null || exitMenuItems.Length != 1 || !clockMenu.Descendants().Contains(exitMenuItems[0]) ||
    appBarMenu.Descendants().Contains(exitMenuItems[0]))
    throw new Exception("Exit menu item must appear only in the clock context menu.");
Console.WriteLine("PASS: Exit menu is available only from the clock context menu.");
var generatedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "artifacts", "worktrees" };
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
var placementGuard = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.WindowPlacementGuard");
var planPlacement = placementGuard?.GetMethod("PlanPlacement", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (planPlacement == null) throw new Exception("Window placement policy is missing.");
System.Windows.Rect? PlanWindow(ManagedShell.Interop.NativeMethods.Rect visible, System.Windows.Rect area, bool maximized) =>
    (System.Windows.Rect?)planPlacement.Invoke(null, new object[] { visible, area, maximized });
var reservedArea = new System.Windows.Rect(158, 41, 1604, 998);
var oversizedWindow = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
if (PlanWindow(oversizedWindow, reservedArea, true).HasValue)
    throw new Exception("Maximized windows must retain Windows-managed placement and restore bounds.");
var normalWindow = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 900, Bottom = 700 };
var relocated = PlanWindow(normalWindow, reservedArea, false);
if (!relocated.HasValue || relocated.Value.X != 158 || relocated.Value.Y != 41 ||
    relocated.Value.Width != 900 || relocated.Value.Height != 700 ||
    PlanWindow(new ManagedShell.Interop.NativeMethods.Rect { Left = 158, Top = 41, Right = 1058, Bottom = 741 },
        reservedArea, false).HasValue)
    throw new Exception("Restored windows must move out of panel space without changing a fitting size.");
var clipped = PlanWindow(oversizedWindow, reservedArea, false);
if (!clipped.HasValue || clipped.Value != reservedArea)
    throw new Exception("An oversized restored window must fit the reserved work area.");
Console.WriteLine("PASS: maximized placement is untouched; restored windows move without unnecessary resizing.");
var workAreaManager = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.WorkAreaManager");
var isCurrentWorkArea = workAreaManager?.GetMethod("IsCurrent");
var liveWorkArea = new ManagedShell.Interop.NativeMethods.Rect();
if (isCurrentWorkArea == null ||
    !ManagedShell.Interop.NativeMethods.SystemParametersInfo(
        (int)ManagedShell.Interop.NativeMethods.SPI.GETWORKAREA, 0, ref liveWorkArea, 0))
    throw new Exception("Cannot read the current system work area.");
bool IsCurrentWorkArea(ManagedShell.Interop.NativeMethods.Rect expected) =>
    (bool)isCurrentWorkArea.Invoke(null, new object[] { expected });
if (!IsCurrentWorkArea(liveWorkArea)) throw new Exception("The current system work area was not recognized.");
liveWorkArea.Left++;
if (IsCurrentWorkArea(liveWorkArea)) throw new Exception("A lost work area was not detected.");
Console.WriteLine("PASS: system work-area reconciliation detects a changed rectangle without writing system state.");
if (Array.IndexOf(args, "--themes") >= 0) ThemeChecks.Run();
if (ReleaseEndpoints.Releases != "https://github.com/PHPCraftdream/UltraWinBar/releases" ||
    ReleaseEndpoints.LatestReleaseApi != "https://api.github.com/repos/PHPCraftdream/UltraWinBar/releases/latest")
    throw new Exception("Release endpoints do not target this project.");
if (Array.IndexOf(args, "--desktop-interop") >= 0)
{
    using var desktopActions = new DesktopActions();
    var desktops = DesktopActions.GetDesktops();
    if (desktops.Count == 0) throw new Exception("No virtual desktops enumerated.");
    string sessionDesktopPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{System.Diagnostics.Process.GetCurrentProcess().SessionId}\VirtualDesktops";
    using var sessionDesktopKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sessionDesktopPath);
    using var globalDesktopKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
    byte[] currentDesktopBytes = sessionDesktopKey?.GetValue("CurrentVirtualDesktop") as byte[] ??
        globalDesktopKey?.GetValue("CurrentVirtualDesktop") as byte[];
    if (currentDesktopBytes?.Length == 16 && !desktops.Any(desktop => desktop.Id == new Guid(currentDesktopBytes)))
        throw new Exception("The active registry desktop ID is absent from the desktop list.");
    if (currentDesktopBytes?.Length == 16) Console.WriteLine("PASS: active session desktop ID matches the registered desktop list.");
    bool foundView = false;
    foreach (var hwnd in DesktopInteropProbe.VisibleWindows())
    {
        try
        {
            string appId = desktopActions.GetApplicationId(hwnd);
            bool pinned = desktopActions.IsApplicationIdPinned(appId);
            foundView = true;
            Console.WriteLine($"PASS: read-only desktop COM integration; desktops={desktops.Count}, appPinned={pinned}.");
            break;
        }
        catch (COMException error) when (error.HResult == unchecked((int)0x8002802B)) { }
    }
    if (!foundView) Console.WriteLine($"SKIP: {desktops.Count} desktops found, no visible window exposes an application view.");
}
if (Array.IndexOf(args, "--desktop-context-interop") >= 0)
{
    var knownDesktops = DesktopActions.GetDesktops().Select(desktop => desktop.Id).ToArray();
    var context = DesktopInteropProbe.ReadContext(knownDesktops, DesktopInteropProbe.VisibleWindows());
    Console.WriteLine($"PASS: VirtualDesktopContext resolved active desktop and a window desktop; onCurrent={context.IsCurrent}.");
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
string stableWindow = TaskOrderIdentifier.CreateKey(100, 200, 300);
string legacyWindow = "class:Browser|title:Old title";
var windowRules = new List<TaskbarAssignment>
{
    new() { Identifier = legacyWindow, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Left, DesktopId = desktopA },
    new() { Identifier = stableWindow, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Right, DesktopId = desktopA },
    new() { Identifier = "browser", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Bottom },
};
if (TaskAssignmentManager.ResolveEdge(windowRules, desktopA, stableWindow, legacyWindow, "browser") != AppBarEdge.Right ||
    TaskAssignmentManager.ResolveEdge(windowRules, desktopA, "window:v2:100:201:301", legacyWindow, "browser") != AppBarEdge.Left ||
    TaskAssignmentManager.ResolveEdge(windowRules, desktopA, legacyWindow, "browser") != AppBarEdge.Left)
    throw new Exception("Stable-window and legacy-title assignment precedence mismatch.");
rules = JsonSerializer.Deserialize<List<TaskbarAssignment>>(JsonSerializer.Serialize(rules));
Check(desktopA, "window", AppBarEdge.Top);
Check(desktopB, "window", AppBarEdge.Right);
rules.RemoveAll(rule => rule.DesktopId == desktopA);
Check(desktopB, "window", AppBarEdge.Right);
Check(desktopA, "window", AppBarEdge.Bottom);
rules = JsonSerializer.Deserialize<List<TaskbarAssignment>>("[{\"Identifier\":\"browser\",\"Edge\":0,\"Mode\":0}]");
Check(desktopA, "window", AppBarEdge.Left);
Console.WriteLine("PASS: desktop isolation, window precedence, JSON restart round-trip, scoped removal, legacy settings.");
Console.WriteLine("PASS: stable per-window rules outrank legacy title rules while legacy assignments remain readable.");

string persistenceDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-settings-" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(persistenceDirectory);
try
{
    string settingsPath = System.IO.Path.Combine(persistenceDirectory, "settings.json");
    var manager = new SettingsManager<PersistenceFixture>(settingsPath, new PersistenceFixture { Value = "default" });
    manager.Settings = new PersistenceFixture { Value = "intermediate" };
    manager.Settings = new PersistenceFixture { Value = "saved" };
    manager.Flush();
    if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(settingsPath))?.Value != "saved")
        throw new Exception("Coalesced settings save did not persist the newest value.");

    System.IO.File.WriteAllText(settingsPath, "{ invalid json");
    var recovered = new SettingsManager<PersistenceFixture>(settingsPath, new PersistenceFixture { Value = "fallback" });
    if (recovered.Settings.Value != "fallback") throw new Exception("Corrupt settings did not fall back to defaults.");
    recovered.Settings = new PersistenceFixture { Value = "recovered" };
    recovered.Flush();
    if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(settingsPath))?.Value != "recovered" ||
        !System.IO.Directory.EnumerateFiles(persistenceDirectory, "settings.json.corrupt-*").Any(path => System.IO.File.ReadAllText(path) == "{ invalid json"))
        throw new Exception("Corrupt settings backup or atomic recovery save failed.");
}
finally
{
    System.IO.Directory.Delete(persistenceDirectory, true);
}
Console.WriteLine("PASS: settings coalesce writes, flush latest values, fall back safely, and preserve corrupt files.");

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
if (actualDefaults["MoveActivatedWindowsToCurrentDesktop"].GetValue<bool>())
    throw new Exception("Experimental desktop activation must be disabled by default.");
Console.WriteLine("PASS: current general settings are defaults; no personal pins, desktop IDs or assignments embedded.");

var guardType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.DesktopActivationGuard");
var shouldMove = guardType?.GetMethod("ShouldMoveWindow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (shouldMove == null) throw new Exception("Desktop activation guard policy is missing.");
bool CanMove(Guid source, Guid current, Guid owner, bool onCurrent, long age) =>
    (bool)shouldMove.Invoke(null, new object[] { source, current, owner, onCurrent, age });
Guid originDesktop = Guid.NewGuid(), otherDesktop = Guid.NewGuid();
if (!CanMove(originDesktop, originDesktop, otherDesktop, false, 200) ||
    CanMove(originDesktop, otherDesktop, otherDesktop, false, 200) ||
    CanMove(originDesktop, originDesktop, originDesktop, false, 200) ||
    CanMove(originDesktop, originDesktop, otherDesktop, true, 200) ||
    CanMove(originDesktop, originDesktop, otherDesktop, false, 6000))
    throw new Exception("Desktop activation guard may move a window after an intentional switch or stale launch.");
Console.WriteLine("PASS: desktop activation policy only accepts fresh foreign-window activations on the originating desktop.");
var recoverAfterSwitch = guardType.GetMethod("ShouldRecoverAfterSwitch", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var returnAfterMove = guardType.GetMethod("ShouldReturnAfterMove", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
bool CanRecover(Guid source, Guid current, Guid owner, long age, bool inputIntact) =>
    (bool)recoverAfterSwitch.Invoke(null, new object[] { source, current, owner, age, inputIntact });
bool CanReturn(Guid source, Guid original, Guid current, Guid owner, long age, bool inputIntact) =>
    (bool)returnAfterMove.Invoke(null, new object[] { source, original, current, owner, age, inputIntact });
if (!CanRecover(originDesktop, otherDesktop, otherDesktop, 200, true) ||
    CanRecover(originDesktop, otherDesktop, otherDesktop, 200, false) ||
    CanRecover(originDesktop, otherDesktop, originDesktop, 200, true) ||
    CanRecover(originDesktop, otherDesktop, otherDesktop, 6000, true) ||
    !CanReturn(originDesktop, otherDesktop, otherDesktop, originDesktop, 2000, true) ||
    CanReturn(originDesktop, otherDesktop, otherDesktop, originDesktop, 2000, false) ||
    CanReturn(originDesktop, otherDesktop, otherDesktop, otherDesktop, 2000, true) ||
    CanReturn(originDesktop, otherDesktop, originDesktop, originDesktop, 2000, true))
    throw new Exception("Desktop return policy may reverse an intentional switch or follow a stale window.");
Console.WriteLine("PASS: desktop return policy requires the same launch, target window, and originating desktop.");
var uniqueWindowPolicy = guardType.GetMethod("HasUniqueForeignWindow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
bool HasUniqueForeignWindow(int current, int remote) =>
    (bool)uniqueWindowPolicy.Invoke(null, new object[] { current, remote });
if (!HasUniqueForeignWindow(0, 1) || HasUniqueForeignWindow(1, 1) ||
    HasUniqueForeignWindow(0, 2) || HasUniqueForeignWindow(0, 0))
    throw new Exception("Activation could pre-move an unrelated or ambiguous window.");
Console.WriteLine("PASS: pre-move requires exactly one foreign window and no current window.");
if (args.Contains("--shortcut-probe"))
{
    var resolver = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.DesktopShortcutResolver");
    var selection = resolver?.GetMethod("ReadSelectedShortcut").Invoke(null, null);
    if (selection == null) Console.WriteLine("SKIP: no executable desktop shortcut selected.");
    else
    {
        var shortcutPath = (string)selection.GetType().GetProperty("ShortcutPath").GetValue(selection);
        var targetPath = (string)selection.GetType().GetProperty("TargetPath").GetValue(selection);
        if (!System.IO.File.Exists(shortcutPath) || !System.IO.File.Exists(targetPath) ||
            !string.Equals(System.IO.Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Selected desktop shortcut did not resolve to an executable.");
        Console.WriteLine("PASS: selected desktop shortcut resolves to an executable.");
    }
}
var enabledGuardSettings = JsonSerializer.Deserialize("{\"MoveActivatedWindowsToCurrentDesktop\":true}", settingsType);
if (!System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(enabledGuardSettings, settingsType))
    ["MoveActivatedWindowsToCurrentDesktop"].GetValue<bool>())
    throw new Exception("Desktop activation preference did not survive JSON round-trip.");

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

internal sealed class PersistenceFixture : IMigratableSettings
{
    public bool MigrationPerformed => false;
    public string Value { get; set; }
}

internal static class DesktopInteropProbe
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    internal static List<IntPtr> VisibleWindows()
    {
        var result = new List<IntPtr>();
        EnumWindowsProc callback = (hwnd, _) =>
        {
            if (IsWindowVisible(hwnd)) result.Add(hwnd);
            return true;
        };
        if (!EnumWindows(callback, IntPtr.Zero)) throw new InvalidOperationException("EnumWindows failed.");
        return result;
    }

    internal static (Guid CurrentDesktop, bool IsCurrent) ReadContext(Guid[] knownDesktops, List<IntPtr> windows)
    {
        Exception failure = null;
        (Guid CurrentDesktop, bool IsCurrent) result = default;
        var thread = new System.Threading.Thread(() =>
        {
            System.Windows.Application app = null;
            object context = null;
            try
            {
                app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                Type contextType = typeof(DesktopActions).Assembly.GetType("UltraWinBar.Utilities.VirtualDesktopContext");
                context = Activator.CreateInstance(contextType, true);
                var managerField = contextType.GetField("manager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                object manager = managerField?.GetValue(context);
                if (manager == null)
                    throw new InvalidOperationException("IVirtualDesktopManager could not be created.");
                var managerInterface = managerField.FieldType;
                var getDesktopId = managerInterface.GetMethod("GetWindowDesktopId");
                var isWindowOnCurrent = managerInterface.GetMethod("IsWindowOnCurrentVirtualDesktop");

                var currentId = (Guid)contextType.GetProperty("CurrentId").GetValue(context);
                if (currentId == Guid.Empty || !knownDesktops.Contains(currentId))
                    throw new InvalidOperationException("The current registry desktop ID is not in the desktop list.");

                foreach (var hwnd in windows)
                {
                    try
                    {
                        object[] desktopArguments = { hwnd, Guid.Empty };
                        if ((int)getDesktopId.Invoke(manager, desktopArguments) < 0) continue;
                        var desktopId = (Guid)desktopArguments[1];
                        if (desktopId == Guid.Empty || !knownDesktops.Contains(desktopId)) continue;
                        object[] currentArguments = { hwnd, false };
                        if ((int)isWindowOnCurrent.Invoke(manager, currentArguments) < 0) continue;
                        bool isCurrent = (bool)currentArguments[1];
                        if ((Guid)contextType.GetMethod("DesktopForWindow").Invoke(context, new object[] { hwnd }) != desktopId ||
                            (bool)contextType.GetMethod("IsOnCurrentDesktop").Invoke(context, new object[] { hwnd }) != isCurrent)
                            throw new InvalidOperationException("VirtualDesktopContext disagrees with IVirtualDesktopManager.");
                        result = (currentId, isCurrent);
                        return;
                    }
                    catch (System.Reflection.TargetInvocationException error) when (error.InnerException is COMException) { }
                }

                throw new InvalidOperationException("No visible window had a registered virtual desktop ID.");
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                try { (context as IDisposable)?.Dispose(); }
                catch (Exception error) { failure ??= error; }
                try { app?.Shutdown(); }
                catch (Exception error) { failure ??= error; }
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("VirtualDesktopContext integration failed.", failure);
        return result;
    }
}
