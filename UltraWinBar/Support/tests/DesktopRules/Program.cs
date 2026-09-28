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

StructureChecks.Run(args, repositoryRoot);
LayoutChecks.Run(args, repositoryRoot);
EnvironmentChecks.Run(args, repositoryRoot);
AssignmentChecks.Run(args, repositoryRoot);
ClockChecks.Run(args, repositoryRoot);
PersistenceChecks.Run(args, repositoryRoot);
ActivationChecks.Run(args, repositoryRoot);
PinChecks.Run(args, repositoryRoot);
UptimeChecks.Run();
InteropChecks.Run();
TaskOrderChecks.Run(args, repositoryRoot);
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
