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
var restartMenuItems = taskbarMenus.Descendants()
    .Where(element => element.Name.LocalName == "StaticResourceExtension" &&
        (string)element.Attribute("ResourceKey") == "RestartAppMenuItem").ToArray();
var restartMenuResource = taskbarMenus.Descendants(presentation + "MenuItem")
    .SingleOrDefault(element => (string)element.Attribute(xaml + "Key") == "RestartAppMenuItem");
if (restartMenuItems.Length != 1 || !clockMenu.Descendants().Contains(restartMenuItems[0]) ||
    (string)restartMenuResource?.Attribute("Click") != "RestartMenuItem_Click" ||
    (string)restartMenuResource.Attribute("Header") != "{DynamicResource restart_ultrawinbar}")
    throw new Exception("Clock context menu must offer restarting UltraWinBar.");
Console.WriteLine("PASS: Exit and Restart menu items are available from the clock context menu; Exit only there.");
var generatedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", ".claude", "bin", "obj", "artifacts", "worktrees", "checkpoints" };
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
var recoveryType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.WorkAreaRecovery");
var recoverMethod = recoveryType?.GetMethod("Recover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
if (recoverMethod == null) throw new Exception("Work-area recovery controller is missing.");
object NewRecovery() => Activator.CreateInstance(recoveryType, true);
string RecoverArea(object controller, long time, Func<bool> current, Action apply) =>
    recoverMethod.Invoke(controller, new object[] { time, current, apply }).ToString();
var recovery = NewRecovery();
int workAreaWrites = 0;
Action writeWorkArea = () =>
{
    workAreaWrites++;
    if (RecoverArea(recovery, 0, () => false, () => workAreaWrites++) != "Deferred")
        throw new Exception("A reentrant work-area notification was not suppressed.");
};
if (RecoverArea(recovery, 0, () => true, writeWorkArea) != "Unchanged" || workAreaWrites != 0 ||
    RecoverArea(recovery, 0, () => false, writeWorkArea) != "Applied")
    throw new Exception("Work-area recovery wrote an unchanged area or missed the initial reset.");
long RetryAt(object controller) => (long)recoveryType.GetProperty("RetryAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
long SuspendedFor(object controller) => (long)recoveryType.GetProperty("SuspendedFor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
void ResetRecovery(object controller) => recoveryType.GetMethod("Reset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
foreach (long time in new long[] { 1, 100, 500, 1999 })
    if (RecoverArea(recovery, time, () => false, writeWorkArea) != "Deferred" || RetryAt(recovery) != 2000)
        throw new Exception("A burst of work-area notifications bypassed the cooldown or lost its retry time.");
if (RecoverArea(recovery, 2000, () => false, writeWorkArea) != "Applied" ||
    RecoverArea(recovery, 4000, () => false, writeWorkArea) != "Applied" ||
    RecoverArea(recovery, 6000, () => false, writeWorkArea) != "Suspended" || workAreaWrites != 3 ||
    SuspendedFor(recovery) != 60000 || RetryAt(recovery) != 66000 ||
    RecoverArea(recovery, 30000, () => throw new Exception("Suspended recovery still read OS state."), writeWorkArea) != "Deferred" ||
    RetryAt(recovery) != 66000)
    throw new Exception("Competing work-area changes can cause unbounded desktop resizing.");
if (RecoverArea(recovery, 66000, () => false, writeWorkArea) != "Applied" || workAreaWrites != 4 ||
    RecoverArea(recovery, 68000, () => false, writeWorkArea) != "Suspended" || SuspendedFor(recovery) != 300000 ||
    RecoverArea(recovery, 368000, () => false, writeWorkArea) != "Applied" ||
    RecoverArea(recovery, 370000, () => false, writeWorkArea) != "Suspended" || SuspendedFor(recovery) != 900000 || workAreaWrites != 5)
    throw new Exception("A persistent work-area conflict did not back off with a single probe per pause.");
if (RecoverArea(recovery, 1270000, () => false, writeWorkArea) != "Applied" ||
    RecoverArea(recovery, 1330000, () => false, writeWorkArea) != "Applied" ||
    RecoverArea(recovery, 1332000, () => false, writeWorkArea) != "Applied" ||
    RecoverArea(recovery, 1334000, () => false, writeWorkArea) != "Applied" ||
    RecoverArea(recovery, 1336000, () => false, writeWorkArea) != "Suspended" || SuspendedFor(recovery) != 60000)
    throw new Exception("A probe that held for the quiet period did not restore the full recovery budget.");
ResetRecovery(recovery);
if (RecoverArea(recovery, 1337000, () => false, writeWorkArea) != "Applied" || workAreaWrites != 10)
    throw new Exception("Reopening the panels did not end a work-area recovery pause.");
var delayedRecovery = NewRecovery();
foreach (long time in new long[] { 0, 2000, 4000, 64000 })
    if (RecoverArea(delayedRecovery, time, () => false, () => { }) != "Applied")
        throw new Exception("Isolated work-area resets incorrectly exhausted the recovery budget.");
var failedRecovery = NewRecovery();
try
{
    RecoverArea(failedRecovery, 0, () => false, () => throw new InvalidOperationException("Rejected work-area write."));
    throw new Exception("Expected the injected work-area write to fail.");
}
catch (System.Reflection.TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
if (RecoverArea(failedRecovery, 2000, () => false, () => { }) != "Applied")
    throw new Exception("A failed write permanently held the recovery reentrancy guard.");
Console.WriteLine("PASS: repeated and reentrant work-area resets are bounded; conflicts pause writes with escalating backoff and retry after each pause.");
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

var clockType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Controls.Clock");
var canShowSecondsMethod = clockType.GetMethod("ClockCanShowSeconds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var nextTickDelayMethod = clockType.GetMethod("GetNextTickDelay", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
bool CanShowSeconds(bool showSeconds, bool overrideFormat, string format) =>
    (bool)canShowSecondsMethod.Invoke(null, new object[] { showSeconds, overrideFormat, format });
TimeSpan NextTickDelay(DateTime now, bool secondsVisible) =>
    (TimeSpan)nextTickDelayMethod.Invoke(null, new object[] { now, secondsVisible });
if (!CanShowSeconds(true, false, null) ||
    CanShowSeconds(false, false, null) ||
    !CanShowSeconds(false, true, "h:mm:ss tt") ||
    CanShowSeconds(false, true, "h:mm tt") ||
    CanShowSeconds(false, false, "h:mm:ss tt"))
    throw new Exception("Clock seconds-visibility rule mismatch.");
Console.WriteLine("PASS: clock format can show seconds only via ShowClockSeconds or an override format containing 's'.");
DateTime midSecond = new DateTime(2026, 1, 1, 12, 0, 30, 500);
TimeSpan delayWithSeconds = NextTickDelay(midSecond, true);
TimeSpan delayWithoutSeconds = NextTickDelay(midSecond, false);
if (delayWithSeconds <= TimeSpan.FromMilliseconds(500) || delayWithSeconds >= TimeSpan.FromMilliseconds(600))
    throw new Exception($"Seconds-visible tick should re-arm for the next second, got {delayWithSeconds}.");
if (delayWithoutSeconds <= TimeSpan.FromMilliseconds(29500) || delayWithoutSeconds >= TimeSpan.FromMilliseconds(29600))
    throw new Exception($"Seconds-hidden tick should re-arm for the next minute, got {delayWithoutSeconds}.");
DateTime nearRollover = new DateTime(2026, 1, 1, 12, 0, 59, 990);
TimeSpan delayNearRollover = NextTickDelay(nearRollover, true);
if (delayNearRollover <= TimeSpan.FromMilliseconds(10) || delayNearRollover >= TimeSpan.FromMilliseconds(60))
    throw new Exception($"Tick just before a second boundary should fire ~10ms later plus a small margin, got {delayNearRollover}.");
DateTime onBoundary = new DateTime(2026, 1, 1, 12, 1, 0, 0);
TimeSpan delayOnBoundarySeconds = NextTickDelay(onBoundary, true);
TimeSpan delayOnBoundaryMinutes = NextTickDelay(onBoundary, false);
if (delayOnBoundarySeconds <= TimeSpan.FromSeconds(1) || delayOnBoundarySeconds >= TimeSpan.FromMilliseconds(1100))
    throw new Exception($"Tick exactly on a second boundary should re-arm a full second later, got {delayOnBoundarySeconds}.");
if (delayOnBoundaryMinutes <= TimeSpan.FromMinutes(1) || delayOnBoundaryMinutes >= TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(100))
    throw new Exception($"Tick exactly on a minute boundary should re-arm a full minute later, got {delayOnBoundaryMinutes}.");
Console.WriteLine("PASS: clock re-arms for the next second boundary when seconds are visible and the next minute boundary otherwise, landing just after it with a small margin.");

var virtualDesktopContextType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.VirtualDesktopContext");
var buildSessionPathMethod = virtualDesktopContextType.GetMethod("BuildSessionPath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
string builtSessionPath = (string)buildSessionPathMethod.Invoke(null, new object[] { 7 });
if (builtSessionPath != @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\7\VirtualDesktops")
    throw new Exception("Session desktop registry path formula changed: " + builtSessionPath);
var treeWatchType = virtualDesktopContextType.GetNestedType("RegistryTreeWatch", System.Reflection.BindingFlags.NonPublic);
var parentPathMethod = treeWatchType.GetMethod("ParentPath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var ancestorChainFromMethod = treeWatchType.GetMethod("AncestorChainFrom", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
string ParentOf(string path) => (string)parentPathMethod.Invoke(null, new object[] { path });
string[] AncestorsOf(string path) => ((System.Collections.IEnumerable)ancestorChainFromMethod.Invoke(null, new object[] { path })).Cast<string>().ToArray();
if (ParentOf(builtSessionPath) != @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\7" ||
    ParentOf("NoBackslashHere") != null)
    throw new Exception("Registry watch parent-path computation is wrong.");
var expectedAncestorChain = new[]
{
    @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\7",
    @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo",
    @"Software\Microsoft\Windows\CurrentVersion\Explorer",
    @"Software\Microsoft\Windows\CurrentVersion",
    @"Software\Microsoft\Windows",
    @"Software\Microsoft",
    @"Software",
};
if (!AncestorsOf(builtSessionPath).SequenceEqual(expectedAncestorChain))
    throw new Exception("Registry watch ancestor chain is wrong: " + string.Join(" | ", AncestorsOf(builtSessionPath)));
Console.WriteLine("PASS: virtual-desktop session registry path is computed once from a cached session ID, and its watch climbs to the nearest existing ancestor so a missing-at-start or Explorer-recreated desktop key is picked back up.");

string taskButtonSource = System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot.FullName,
    "UltraWinBar", "UI", "Tasks", "Buttons", "TaskButton.xaml.cs"));
int taskButtonLoadedStart = taskButtonSource.IndexOf("private void TaskButton_OnLoaded");
int taskButtonLoadedEnd = taskButtonSource.IndexOf("private void Window_GetButtonRect", taskButtonLoadedStart);
if (taskButtonLoadedStart < 0 || taskButtonLoadedEnd < 0)
    throw new Exception("Could not locate TaskButton_OnLoaded to check its idempotency guard.");
string taskButtonLoadedBody = taskButtonSource.Substring(taskButtonLoadedStart, taskButtonLoadedEnd - taskButtonLoadedStart);
int isLoadedGuardIndex = taskButtonLoadedBody.IndexOf("if (_isLoaded)");
int settingsSubscribeIndex = taskButtonLoadedBody.IndexOf("Settings.Instance.PropertyChanged += Settings_PropertyChanged;");
int dragHandlerCreateIndex = taskButtonLoadedBody.IndexOf("dragHandler = new DelayedActivationHandler");
int animateCallIndex = taskButtonLoadedBody.IndexOf("Animate();");
if (isLoadedGuardIndex < 0 || settingsSubscribeIndex < 0 || dragHandlerCreateIndex < 0 || animateCallIndex < 0 ||
    isLoadedGuardIndex > settingsSubscribeIndex || isLoadedGuardIndex > dragHandlerCreateIndex || isLoadedGuardIndex > animateCallIndex)
    throw new Exception("TaskButton_OnLoaded must return early when already loaded, before re-subscribing, replacing dragHandler, or replaying the slide-in animation.");
if (System.Text.RegularExpressions.Regex.Matches(taskButtonLoadedBody, @"_isLoaded\s*=\s*true").Count != 1)
    throw new Exception("TaskButton_OnLoaded should set _isLoaded exactly once, after the idempotency guard.");
Console.WriteLine("PASS: a repeated TaskButton Loaded without an intervening Unloaded is a no-op and does not re-subscribe, replace dragHandler, or replay the slide-in animation.");

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

string debounceDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-settings-debounce-" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(debounceDirectory);
try
{
    var managerType = typeof(SettingsManager<PersistenceFixture>);
    var serializeCountField = managerType.GetField("SerializeCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var saveTimerElapsedMethod = managerType.GetMethod("SaveTimerElapsed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    long SerializeCountOf(object instance) => (long)serializeCountField.GetValue(instance);

    // A burst of changes must serialize once (on flush), not once per change.
    string burstPath = System.IO.Path.Combine(debounceDirectory, "burst.json");
    var burstManager = new SettingsManager<PersistenceFixture>(burstPath, new PersistenceFixture { Value = "default" });
    for (int i = 0; i < 5; i++)
        burstManager.Settings = new PersistenceFixture { Value = $"v{i}" };
    if (SerializeCountOf(burstManager) != 0)
        throw new Exception($"A burst of settings changes must not serialize before flush/debounce; serialized {SerializeCountOf(burstManager)} times.");
    burstManager.Flush();
    if (SerializeCountOf(burstManager) != 1)
        throw new Exception($"A burst of settings changes must serialize exactly once on flush; serialized {SerializeCountOf(burstManager)} times.");
    if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(burstPath))?.Value != "v4")
        throw new Exception("Flush after a burst did not persist the latest value.");

    // Flush must persist the latest value synchronously even over a pending debounce.
    burstManager.Settings = new PersistenceFixture { Value = "pending" };
    burstManager.Flush();
    if (SerializeCountOf(burstManager) != 2)
        throw new Exception("Flush over a pending debounce should serialize exactly once more.");
    if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(burstPath))?.Value != "pending")
        throw new Exception("Flush did not persist the latest value over a pending debounce.");

    var previousContext = System.Threading.SynchronizationContext.Current;
    try
    {
        // No captured SynchronizationContext (console/tests): debounce elapsing must
        // serialize inline rather than posting anywhere.
        System.Threading.SynchronizationContext.SetSynchronizationContext(null);
        string fallbackPath = System.IO.Path.Combine(debounceDirectory, "fallback.json");
        var fallbackManager = new SettingsManager<PersistenceFixture>(fallbackPath, new PersistenceFixture { Value = "default" });
        fallbackManager.Settings = new PersistenceFixture { Value = "fallback-value" };
        if (SerializeCountOf(fallbackManager) != 0)
            throw new Exception("A settings change must not serialize before the debounce fires, even with no captured context.");
        saveTimerElapsedMethod.Invoke(fallbackManager, new object[] { null });
        if (SerializeCountOf(fallbackManager) != 1)
            throw new Exception("Debounce elapsing with no captured SynchronizationContext must serialize inline.");
        fallbackManager.Flush();
        if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(fallbackPath))?.Value != "fallback-value")
            throw new Exception("No-context fallback did not persist the debounced value.");

        // Settings is constructed before the UI dispatcher runs; the first change made under a
        // context must adopt it, and serialization must be posted there, not run inline.
        var recordingContext = new RecordingSyncContext();
        string postedPath = System.IO.Path.Combine(debounceDirectory, "posted.json");
        var postedManager = new SettingsManager<PersistenceFixture>(postedPath, new PersistenceFixture { Value = "default" });
        System.Threading.SynchronizationContext.SetSynchronizationContext(recordingContext);
        postedManager.Settings = new PersistenceFixture { Value = "posted-value" };
        System.Threading.SynchronizationContext.SetSynchronizationContext(null);
        saveTimerElapsedMethod.Invoke(postedManager, new object[] { null });
        if (recordingContext.PostCount != 1 || SerializeCountOf(postedManager) != 0)
            throw new Exception("Debounce elapsing must post serialization to the owner context adopted from the first change, not run inline.");
        recordingContext.Pump();
        if (SerializeCountOf(postedManager) != 1)
            throw new Exception("Posting to the owner context did not serialize the settings.");
        postedManager.Flush();
        if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(postedPath))?.Value != "posted-value")
            throw new Exception("Serialization posted to the owner context did not persist the latest value.");
    }
    finally
    {
        System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
    }
}
finally
{
    System.IO.Directory.Delete(debounceDirectory, true);
}
Console.WriteLine("PASS: settings changes debounce serialization to once per burst, flush forces it inline, and the no-context/posted-context paths each serialize exactly once.");

var loggerType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.ManagedShellLogger");
var retentionField = loggerType.GetField("LogRetention", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
if ((TimeSpan)retentionField.GetValue(null) != TimeSpan.FromDays(7))
    throw new Exception("Log retention must be 7 days.");

var rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
var shouldRollMethod = rollingLogType.GetMethod("ShouldRoll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var buildNameMethod = rollingLogType.GetMethod("BuildLogFileName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
bool RollAt(long size, long cap) => (bool)shouldRollMethod.Invoke(null, new object[] { size, cap });
if (RollAt(19 * 1024 * 1024, 20 * 1024 * 1024) || !RollAt(20 * 1024 * 1024, 20 * 1024 * 1024) || !RollAt(21 * 1024 * 1024, 20 * 1024 * 1024))
    throw new Exception("Roll-over decision must trigger once size reaches the cap, not before.");
var rollTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, 6);
string firstRollName = (string)buildNameMethod.Invoke(null, new object[] { rollTimestamp, 0 });
string secondRollName = (string)buildNameMethod.Invoke(null, new object[] { rollTimestamp, 1 });
if (firstRollName == secondRollName)
    throw new Exception("Successive rolls within the same process must produce unique file names even with identical timestamps.");
Console.WriteLine("PASS: log retention is 7 days and the roll-over decision and file naming are deterministic and collision-free.");

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

var completesDoubleClick = guardType.GetMethod("CompletesDoubleClick", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (completesDoubleClick == null) throw new Exception("Double-click completion decision is missing.");
bool CompletesDoubleClick(bool previousOnDesktop, long previousAt, long now, int previousX, int previousY,
    int x, int y, uint doubleClickMs, int xTolerance, int yTolerance) =>
    (bool)completesDoubleClick.Invoke(null, new object[]
        { previousOnDesktop, previousAt, now, previousX, previousY, x, y, doubleClickMs, xTolerance, yTolerance });
if (CompletesDoubleClick(false, 0, 100, 10, 10, 12, 11, 500, 4, 4))
    throw new Exception("A completed double-click was recognized without a preceding first click.");
if (!CompletesDoubleClick(true, 0, 100, 10, 10, 12, 11, 500, 4, 4))
    throw new Exception("A same-position click inside the double-click window and tolerance was not recognized.");
if (CompletesDoubleClick(true, 0, 600, 10, 10, 12, 11, 500, 4, 4))
    throw new Exception("A click after the double-click window elapsed was still recognized.");
if (CompletesDoubleClick(true, 0, 100, 10, 10, 20, 11, 500, 4, 4))
    throw new Exception("A click outside the position tolerance was still recognized.");

var hookThreadType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.DesktopActivationHookThread");
if (hookThreadType == null) throw new Exception("Desktop activation hook thread host is missing.");
object CreateHookThread(Func<bool> install, Action uninstall) =>
    Activator.CreateInstance(hookThreadType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
        null, new object[] { install, uninstall }, null);
bool ThrowsFromFailedInstall(Func<bool> install)
{
    try { CreateHookThread(install, () => { }); }
    catch (System.Reflection.TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return true; }
    return false;
}
if (!ThrowsFromFailedInstall(() => false))
    throw new Exception("Hook thread host did not throw when install returned false.");
if (!ThrowsFromFailedInstall(() => throw new InvalidOperationException("install failed")))
    throw new Exception("Hook thread host did not surface an install exception.");

int callerThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
int installThreadId = -1, uninstallThreadId = -1;
bool uninstallCalled = false;
object host = CreateHookThread(
    () => { installThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId; return true; },
    () => { uninstallThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId; uninstallCalled = true; });
if (installThreadId == -1) throw new Exception("Ctor returned before the install callback completed.");
if (installThreadId == callerThreadId) throw new Exception("Install callback ran on the caller thread instead of a dedicated host thread.");
var disposeStopwatch = System.Diagnostics.Stopwatch.StartNew();
((IDisposable)host).Dispose();
disposeStopwatch.Stop();
if (disposeStopwatch.ElapsedMilliseconds > 1500)
    throw new Exception("Disposing the hook thread host took too long; WM_QUIT may not have reached the message loop.");
if (!uninstallCalled) throw new Exception("Disposing the hook thread host did not run the uninstall callback.");
if (uninstallThreadId != installThreadId) throw new Exception("Uninstall did not run on the same thread that installed the hook.");
((IDisposable)host).Dispose();
Console.WriteLine("PASS: double-click completion is a pure per-click decision; the hook thread host waits for install, runs callbacks off the caller thread, and disposes idempotently without deadlock.");
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

var pinsRestoreType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.PersistentDesktopPins");
var requiresPinRestoreMethod = pinsRestoreType?.GetMethod("RequiresPinRestore", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (requiresPinRestoreMethod == null) throw new Exception("Desktop pin restore trigger policy is missing.");
bool RequiresPinRestore(System.Collections.Specialized.NotifyCollectionChangedAction action) =>
    (bool)requiresPinRestoreMethod.Invoke(null, new object[] { action });
if (!RequiresPinRestore(System.Collections.Specialized.NotifyCollectionChangedAction.Add) ||
    !RequiresPinRestore(System.Collections.Specialized.NotifyCollectionChangedAction.Reset) ||
    RequiresPinRestore(System.Collections.Specialized.NotifyCollectionChangedAction.Remove) ||
    RequiresPinRestore(System.Collections.Specialized.NotifyCollectionChangedAction.Replace) ||
    RequiresPinRestore(System.Collections.Specialized.NotifyCollectionChangedAction.Move))
    throw new Exception("Desktop pin restore must trigger only on window-list additions or a full reset, never on activation.");
Console.WriteLine("PASS: desktop pin restore triggers only for added windows or an Explorer reset, never on window activation.");

var shouldFinalizeImportScanMethod = pinsRestoreType?.GetMethod("ShouldFinalizeImportScan", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
if (shouldFinalizeImportScanMethod == null) throw new Exception("Desktop pin import scan bounding policy is missing.");
bool ShouldFinalizeImportScan(bool scanComplete, int attempts, int maxAttempts) =>
    (bool)shouldFinalizeImportScanMethod.Invoke(null, new object[] { scanComplete, attempts, maxAttempts });
if (!ShouldFinalizeImportScan(true, 1, 5) || !ShouldFinalizeImportScan(true, 0, 5))
    throw new Exception("A completed import scan must finalize immediately regardless of attempt count.");
if (ShouldFinalizeImportScan(false, 1, 5) || ShouldFinalizeImportScan(false, 4, 5))
    throw new Exception("An incomplete import scan must keep retrying while the virtual desktop service may still be starting up.");
if (!ShouldFinalizeImportScan(false, 5, 5) || !ShouldFinalizeImportScan(false, 6, 5))
    throw new Exception("An import scan stuck on a persistently failing window must give up once the attempt limit is reached.");
Console.WriteLine("PASS: desktop pin import scan retries an incomplete pass until the attempt limit, then finalizes with the partial result.");

var taskRecoveryType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.TaskWindowRecovery");
var canStyleAddMethod = taskRecoveryType?.GetMethod("CanStyleAddToTaskbar", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var requiresPanelRefreshMethod = taskRecoveryType?.GetMethod("RequiresPanelRefresh", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (canStyleAddMethod == null || requiresPanelRefreshMethod == null)
    throw new Exception("Task window recovery style prefilter or panel-refresh policy is missing.");
bool CanStyleAddToTaskbar(int extendedStyle, bool hasNoOwner, bool taskListNotDeleted) =>
    (bool)canStyleAddMethod.Invoke(null, new object[] { extendedStyle, hasNoOwner, taskListNotDeleted });
const int wsExToolWindow = 0x80, wsExAppWindow = 0x40000, wsExNoActivate = 0x8000000;
if (!CanStyleAddToTaskbar(0, true, true) ||
    CanStyleAddToTaskbar(wsExToolWindow, true, true) ||
    CanStyleAddToTaskbar(wsExToolWindow | wsExAppWindow, true, true) ||
    CanStyleAddToTaskbar(0, false, true) ||
    !CanStyleAddToTaskbar(wsExAppWindow, false, true) ||
    CanStyleAddToTaskbar(wsExNoActivate, true, true) ||
    !CanStyleAddToTaskbar(wsExNoActivate | wsExAppWindow, true, true) ||
    CanStyleAddToTaskbar(0, true, false))
    throw new Exception("Taskbar-eligibility prefilter diverges from ApplicationWindow.CanAddToTaskbar's owned/tool-window/no-activate/deleted rules.");
bool RequiresPanelRefresh(bool windowAdded, bool showInTaskbarChanged) =>
    (bool)requiresPanelRefreshMethod.Invoke(null, new object[] { windowAdded, showInTaskbarChanged });
if (RequiresPanelRefresh(false, false) || !RequiresPanelRefresh(true, false) ||
    !RequiresPanelRefresh(false, true) || !RequiresPanelRefresh(true, true))
    throw new Exception("Panel refresh must be skipped unless a window was added or its ShowInTaskbar value changed.");
Console.WriteLine("PASS: task window recovery prefilters window styles exactly like CanAddToTaskbar and refreshes panels only on an actual change.");
UptimeChecks.Run();

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

var pruneEntriesMethod = typeof(TaskOrderIdentifier).GetMethod("PruneDeadWindowEntries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var pruneAssignmentsMethod = typeof(TaskOrderIdentifier).GetMethod("PruneDeadWindowAssignments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var reconcileMethod = typeof(TaskOrderIdentifier).GetMethod("ReconcilePrimaryWindowKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (pruneEntriesMethod == null || pruneAssignmentsMethod == null || reconcileMethod == null)
    throw new Exception("TaskOrder maintenance functions are missing.");
string liveWindowA = TaskOrderIdentifier.CreateKey(1, 100, 10);
string liveWindowB = TaskOrderIdentifier.CreateKey(2, 200, 20);
string closedWindow = TaskOrderIdentifier.CreateKey(3, 300, 30);
var liveWindowKeys = new HashSet<string> { liveWindowA, liveWindowB };
var taskOrderEntries = new List<TaskOrderEntry>
{
    new() { Edge = AppBarEdge.Bottom, DesktopId = desktopA, Identifier = "pin:exe:browser" },
    new() { Edge = AppBarEdge.Bottom, DesktopId = desktopA, Identifier = closedWindow },
    new() { Edge = AppBarEdge.Left, DesktopId = desktopB, Identifier = liveWindowA },
    new() { Edge = AppBarEdge.Left, DesktopId = desktopB, Identifier = "exe:app.exe#2" },
    new() { Edge = AppBarEdge.Top, DesktopId = Guid.Empty, Identifier = closedWindow },
    new() { Edge = AppBarEdge.Top, DesktopId = Guid.Empty, Identifier = liveWindowB },
    new() { Edge = AppBarEdge.Right, DesktopId = desktopA, Identifier = "class:Notepad|title:Untitled" },
};
var prunedTaskOrder = (List<TaskOrderEntry>)pruneEntriesMethod.Invoke(null, new object[] { taskOrderEntries, liveWindowKeys });
if (prunedTaskOrder == null) throw new Exception("Dead window TaskOrder entries across multiple scopes were not pruned.");
if (!prunedTaskOrder.Select(entry => entry.Identifier).SequenceEqual(
    new[] { "pin:exe:browser", liveWindowA, "exe:app.exe#2", liveWindowB, "class:Notepad|title:Untitled" }))
    throw new Exception("TaskOrder pruning changed relative order or dropped a pin/legacy/exe/live-window key.");
if (!prunedTaskOrder.Any(entry => entry.Edge == AppBarEdge.Left && entry.DesktopId == desktopB && entry.Identifier == liveWindowA) ||
    !prunedTaskOrder.Any(entry => entry.Edge == AppBarEdge.Top && entry.DesktopId == Guid.Empty && entry.Identifier == liveWindowB))
    throw new Exception("TaskOrder pruning must remove dead window keys across every edge/desktop scope at once.");
if (pruneEntriesMethod.Invoke(null, new object[] { prunedTaskOrder, liveWindowKeys }) != null)
    throw new Exception("Re-pruning an already-clean TaskOrder list must be a no-op.");
var taskbarAssignments = new List<TaskbarAssignment>
{
    new() { Identifier = "exe:app.exe", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Bottom, DesktopId = desktopA },
    new() { Identifier = closedWindow, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Left, DesktopId = desktopA },
    new() { Identifier = liveWindowA, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Right, DesktopId = desktopB },
    new() { Identifier = "class:Notepad|title:Untitled", Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Top, DesktopId = Guid.Empty },
    new() { Identifier = liveWindowB, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Bottom, DesktopId = desktopB },
};
var prunedAssignments = (List<TaskbarAssignment>)pruneAssignmentsMethod.Invoke(null, new object[] { taskbarAssignments, liveWindowKeys });
if (prunedAssignments == null) throw new Exception("Dead per-window TaskbarAssignments were not pruned.");
if (!prunedAssignments.Select(assignment => assignment.Identifier).SequenceEqual(
    new[] { "exe:app.exe", liveWindowA, "class:Notepad|title:Untitled", liveWindowB }))
    throw new Exception("TaskbarAssignments pruning changed relative order or dropped a live-window/legacy/exe assignment.");
if (pruneAssignmentsMethod.Invoke(null, new object[] { prunedAssignments, liveWindowKeys }) != null)
    throw new Exception("Re-pruning an already-clean TaskbarAssignments list must be a no-op.");
string ReconcilePrimaryWindowKey(string primary, string selected, HashSet<string> live) =>
    (string)reconcileMethod.Invoke(null, new object[] { primary, selected, live });
if (ReconcilePrimaryWindowKey(closedWindow, liveWindowA, liveWindowKeys) != liveWindowA)
    throw new Exception("A pin bound to a closed window did not adopt the newly selected live window.");
if (ReconcilePrimaryWindowKey(null, liveWindowA, liveWindowKeys) != liveWindowA)
    throw new Exception("A pin with no recorded window did not adopt the newly selected live window.");
if (ReconcilePrimaryWindowKey(liveWindowA, liveWindowA, liveWindowKeys) != liveWindowA)
    throw new Exception("Pin-key reconciliation is not idempotent for an already-live primary key.");
if (ReconcilePrimaryWindowKey(liveWindowA, liveWindowB, liveWindowKeys) != liveWindowA)
    throw new Exception("Pin-key reconciliation must not flip between two live windows of the same pinned application.");
if (ReconcilePrimaryWindowKey(closedWindow, null, liveWindowKeys) != closedWindow)
    throw new Exception("Pin-key reconciliation must not discard the recorded key when no window is currently selected.");
var liveKeysMethod = typeof(TaskOrderIdentifier).GetMethod("LiveKeys", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
using (var undiscoveredWindow = new System.Windows.Forms.Form())
{
    uint ownProcessId = (uint)Environment.ProcessId;
    string undiscoveredKey = TaskOrderIdentifier.CreateKey(ownProcessId, 1, undiscoveredWindow.Handle.ToInt64());
    string foreignOwnerKey = TaskOrderIdentifier.CreateKey(ownProcessId + 1, 1, undiscoveredWindow.Handle.ToInt64());
    string destroyedKey = TaskOrderIdentifier.CreateKey(ownProcessId, 1, 0x7FFFFFF0);
    var storedLiveKeys = (HashSet<string>)liveKeysMethod.Invoke(null, new object[] { null,
        new[] { undiscoveredKey, foreignOwnerKey, destroyedKey, "pin:exe:C:\\app.exe", null } });
    if (!storedLiveKeys.SetEquals(new[] { undiscoveredKey }))
        throw new Exception("Stored window keys must stay live while their window still exists, even before the task source discovers it.");
}
Console.WriteLine("PASS: dead window keys are pruned from every TaskOrder/TaskbarAssignments scope while pins, legacy, exe, and live-window entries keep their relative order and pruning is idempotent; pin-key reconciliation adopts a newly live window once, stays idempotent, and never flips between two live windows.");

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

// RemoveLanguageDictionaries only inspects each dictionary's Source path, so probe
// dictionaries can carry a fabricated Source without loading real XAML content.
var resourceDictionarySourceField = typeof(System.Windows.ResourceDictionary).GetField("_source",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (resourceDictionarySourceField == null) throw new Exception("ResourceDictionary._source field is missing.");
System.Windows.ResourceDictionary MakeProbeDictionary(string relativePath)
{
    var dictionary = new System.Windows.ResourceDictionary();
    resourceDictionarySourceField.SetValue(dictionary, new Uri(System.IO.Path.Combine(@"C:\UltraWinBar", relativePath), UriKind.Absolute));
    return dictionary;
}
var removeLanguageDictionariesMethod = typeof(DictionaryManager).GetMethod("RemoveLanguageDictionaries",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (removeLanguageDictionariesMethod == null) throw new Exception("DictionaryManager.RemoveLanguageDictionaries is missing.");
var languageProbeDictionaries = new System.Collections.ObjectModel.Collection<System.Windows.ResourceDictionary>();
var themeProbeDictionary = MakeProbeDictionary(@"Themes\Core\System.xaml");
languageProbeDictionaries.Add(themeProbeDictionary);
// Simulate repeated language switches: each one removes the previous language
// dictionaries before adding the next, mirroring SetLanguageFromSettings.
foreach (string languageProbeFile in new[] { @"Languages\English.xaml", @"Languages\Group02\Russian.xaml", @"Languages\Group01\German.xaml" })
{
    removeLanguageDictionariesMethod.Invoke(null, new object[] { languageProbeDictionaries });
    languageProbeDictionaries.Add(MakeProbeDictionary(languageProbeFile));
    if (languageProbeDictionaries.Count != 2 || languageProbeDictionaries[0] != themeProbeDictionary ||
        languageProbeDictionaries[1].Source.LocalPath != System.IO.Path.Combine(@"C:\UltraWinBar", languageProbeFile))
        throw new Exception("A language switch left stale language dictionaries merged, or disturbed unrelated dictionary order.");
}
Console.WriteLine("PASS: repeated language switches remove the previous language dictionaries before adding the next, so exactly one language set stays merged in the same order.");

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

var startMenuMonitorType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.StartMenuMonitor");
var shouldHookMenuEvents = startMenuMonitorType?.GetMethod("ShouldHookMenuEvents", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (shouldHookMenuEvents == null) throw new Exception("Start menu event hook policy is missing.");
bool ShouldHookMenuEvents(bool hasPlacement, bool hasActivatedTaskbar) =>
    (bool)shouldHookMenuEvents.Invoke(null, new object[] { hasPlacement, hasActivatedTaskbar });
if (ShouldHookMenuEvents(false, false) || !ShouldHookMenuEvents(true, false) ||
    !ShouldHookMenuEvents(false, true) || !ShouldHookMenuEvents(true, true))
    throw new Exception("Menu event hook must stay installed exactly while placement correction or a pending taskbar activation needs it.");
Console.WriteLine("PASS: Start menu WinEvent hook is required only while placement correction or a pending taskbar activation needs it, not permanently.");

internal sealed class PersistenceFixture : IMigratableSettings
{
    public bool MigrationPerformed => false;
    public string Value { get; set; }
}

// Stand-in for a UI dispatcher: records what was posted without running it until Pump is called.
internal sealed class RecordingSyncContext : System.Threading.SynchronizationContext
{
    public int PostCount;
    private System.Threading.SendOrPostCallback _callback;
    private object _state;

    public override void Post(System.Threading.SendOrPostCallback d, object state)
    {
        PostCount++;
        _callback = d;
        _state = state;
    }

    public void Pump()
    {
        var callback = _callback;
        var state = _state;
        _callback = null;
        callback?.Invoke(state);
    }
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
