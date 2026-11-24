using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// Before anything touches Settings: point it at a scratch file, and remember the user's real file
// so the run can prove it never wrote it.
string userSettingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltraWinBar", "UltraWinBar.settings.json");
DateTime? userSettingsWrite = System.IO.File.Exists(userSettingsPath) ? System.IO.File.GetLastWriteTimeUtc(userSettingsPath) : null;
string scratchSettingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-test-settings-" + Guid.NewGuid().ToString("N") + ".json");
Environment.SetEnvironmentVariable("ULTRAWINBAR_SETTINGS_PATH", scratchSettingsPath);

var repositoryRoot = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
while (repositoryRoot != null && !System.IO.File.Exists(System.IO.Path.Combine(repositoryRoot.FullName, "README.md")))
    repositoryRoot = repositoryRoot.Parent;
if (repositoryRoot == null) throw new Exception("Repository root not found for structure checks.");

// A check group that fails is reported and the others still run, so one run lists every failure; the exit code tells.
var failures = new List<string>();
void Step(string name, Action run)
{
    try { run(); }
    catch (Exception e)
    {
        failures.Add(name + ": " + e.GetType().Name + ": " + e.Message.Split('\n')[0]);
        Console.WriteLine("FAIL: " + name + ": " + e);
    }
}

Step("LoggingChecks", () => LoggingChecks.Run(args, repositoryRoot));
Step("StructureChecks", () => StructureChecks.Run(args, repositoryRoot));
Step("LayoutChecks", () => LayoutChecks.Run(args, repositoryRoot));
Step("EnvironmentChecks", () => EnvironmentChecks.Run(args, repositoryRoot));
Step("AssignmentChecks", () => AssignmentChecks.Run(args, repositoryRoot));
Step("ClockChecks", () => ClockChecks.Run(args, repositoryRoot));
Step("CoordinateChecks", () => CoordinateChecks.Run(args, repositoryRoot));
Step("PlaceChecks", () => PlaceChecks.Run(args, repositoryRoot));
Step("CalendarChecks", () => CalendarChecks.Run(args, repositoryRoot));
Step("HolidayChecks", () => HolidayChecks.Run(args, repositoryRoot));
Step("ZmanimChecks", () => ZmanimChecks.Run(args, repositoryRoot));
Step("YahrzeitChecks", () => YahrzeitChecks.Run(args, repositoryRoot));
Step("CalendarWindowChecks", () => CalendarWindowChecks.Run(args, repositoryRoot));
Step("ModernThemeChecks", () => ModernThemeChecks.Run(args, repositoryRoot));
Step("LanguageChecks", () => LanguageChecks.Run(args, repositoryRoot));
Step("PersistenceChecks", () => PersistenceChecks.Run(args, repositoryRoot));
Step("ActivationChecks", () => ActivationChecks.Run(args, repositoryRoot));
Step("PinChecks", () => PinChecks.Run(args, repositoryRoot));
Step("UptimeChecks", () => UptimeChecks.Run());
Step("InteropChecks", () => InteropChecks.Run(repositoryRoot));
Step("CrossProcessMessageChecks", () => CrossProcessMessageChecks.Run());
Step("NativeCallbackChecks", () => NativeCallbackChecks.Run(repositoryRoot));
Step("HealthDiagnosticsChecks", () => HealthDiagnosticsChecks.Run());
Step("StartupChecks", () => StartupChecks.Run());
Step("TaskOrderAuditChecks", () => TaskOrderAuditChecks.Run());
Step("TaskButtonSizingChecks", () => TaskButtonSizingChecks.Run());
Step("WindowActivityLogChecks", () => WindowActivityLogChecks.Run());
Step("WindowDictionaryChecks", () => WindowDictionaryChecks.Run());
Step("GhostSweepChecks", () => GhostSweepChecks.Run());
Step("StartMenuFadeChecks", () => StartMenuFadeChecks.Run());
Step("TrayHotkeyChecks", () => TrayHotkeyChecks.Run());
Step("ServiceLifecycleChecks", () => ServiceLifecycleChecks.Run(args, repositoryRoot));
Step("TaskOrderChecks", () => TaskOrderChecks.Run(args, repositoryRoot));
Step("IdentifierCacheChecks", () => IdentifierCacheChecks.Run(args, repositoryRoot));
Step("EdgeCacheChecks", () => EdgeCacheChecks.Run(args, repositoryRoot));
Step("TaskGroupingAndDiffChecks", () => TaskGroupingAndDiffChecks.Run(args, repositoryRoot));
Step("LegacyOrdinalBatchChecks", () => LegacyOrdinalBatchChecks.Run(args, repositoryRoot));
Step("TaskModelOrderingChecks", () => TaskModelOrderingChecks.Run(args, repositoryRoot));
Step("TaskModelPinChecks", () => TaskModelPinChecks.Run(args, repositoryRoot));
Step("TaskModelPruningChecks", () => TaskModelPruningChecks.Run(args, repositoryRoot));
Step("TaskModelEquivalenceChecks", () => TaskModelEquivalenceChecks.Run(args, repositoryRoot));
Step("TaskModelHostChecks", () => TaskModelHostChecks.Run(args, repositoryRoot));
Step("DesktopTaskListsChecks", () => DesktopTaskListsChecks.Run(args, repositoryRoot));
const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.NonPublic |
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
settingsType.GetMethod("Flush", hidden).Invoke(null, null);
object settingsManager = settingsType.GetField("_settingsManager", hidden).GetValue(null);
if (!string.Equals((string)settingsManager.GetType().GetField("_fileName", hidden).GetValue(settingsManager),
        scratchSettingsPath, StringComparison.OrdinalIgnoreCase))
    throw new Exception("Settings in the test run did not write to the scratch file.");
// A running UltraWinBar saves its own state to the user's file at any time; only the test's state there is a failure.
bool userSettingsChanged = userSettingsWrite != (System.IO.File.Exists(userSettingsPath) ? System.IO.File.GetLastWriteTimeUtc(userSettingsPath) : null);
if (userSettingsChanged && (!System.IO.File.Exists(userSettingsPath) ||
        System.IO.File.ReadAllText(userSettingsPath) == System.IO.File.ReadAllText(scratchSettingsPath)))
    throw new Exception("The test run modified the user's real settings file.");
System.IO.File.Delete(scratchSettingsPath);
Console.WriteLine(userSettingsChanged
    ? "PASS: the test run kept Settings on a scratch file; the user's settings changed only by the running UltraWinBar."
    : "PASS: the test run kept Settings on a scratch file and never wrote the user's settings.");

if (failures.Count > 0)
{
    Console.WriteLine("FAIL: " + failures.Count + " check group(s) failed:");
    foreach (string failure in failures) Console.WriteLine("FAIL: " + failure);
    return 1;
}
return 0;
