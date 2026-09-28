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
typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings")
    .GetMethod("Flush", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
if (userSettingsWrite != (System.IO.File.Exists(userSettingsPath) ? System.IO.File.GetLastWriteTimeUtc(userSettingsPath) : null))
    throw new Exception("The test run modified the user's real settings file.");
System.IO.File.Delete(scratchSettingsPath);
Console.WriteLine("PASS: the test run kept Settings on a scratch file and never wrote the user's settings.");
