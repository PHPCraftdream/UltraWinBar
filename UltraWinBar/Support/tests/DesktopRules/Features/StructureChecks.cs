using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class StructureChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
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

        // WPF calls Shutdown() (-> App_OnExit -> ExitApp) after a non-cancelled SessionEnding,
        // which already called ExitApp itself; ExitApp must not run its teardown twice, and
        // Shutdown() only requests an async dispatcher shutdown, so each restart-then-FailFast
        // path must flush settings explicitly rather than relying on ExitApp running in time.
        string appSource = System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot.FullName, "UltraWinBar", "Shell", "App.xaml.cs"));
        int exitAppStart = appSource.IndexOf("private void ExitApp()");
        int exitAppEnd = appSource.IndexOf("private static void FlushSettingsBeforeFailFast", exitAppStart);
        if (exitAppStart < 0 || exitAppEnd < 0)
            throw new Exception("Could not locate ExitApp to check idempotency.");
        if (!appSource.Substring(exitAppStart, exitAppEnd - exitAppStart).Contains("Interlocked.Exchange(ref _exitAppRan, 1)"))
            throw new Exception("ExitApp must guard against running twice (SessionEnding, then WPF's own Shutdown->Exit).");
        int sessionEndingStart = appSource.IndexOf("private void App_OnSessionEnding(");
        int sessionEndingEnd = appSource.IndexOf("private void Settings_PropertyChanged", sessionEndingStart);
        if (sessionEndingStart < 0 || sessionEndingEnd < 0)
            throw new Exception("Could not locate App_OnSessionEnding.");
        if (!appSource.Substring(sessionEndingStart, sessionEndingEnd - sessionEndingStart).Contains("SignalGracefulShutdown()"))
            throw new Exception("App_OnSessionEnding must signal graceful shutdown for the restore-work-area watchdog.");
        int dispatcherStart = appSource.IndexOf("private void App_DispatcherUnhandledException(");
        if (dispatcherStart < 0) throw new Exception("Could not locate App_DispatcherUnhandledException.");
        string dispatcherBody = appSource.Substring(dispatcherStart);
        int firstFailFast = dispatcherBody.IndexOf("Environment.FailFast(");
        int secondFailFast = firstFailFast < 0 ? -1 : dispatcherBody.IndexOf("Environment.FailFast(", firstFailFast + 1);
        if (firstFailFast < 0 || secondFailFast < 0)
            throw new Exception("Expected two restart-then-FailFast paths in App_DispatcherUnhandledException.");
        if (dispatcherBody.LastIndexOf("FlushSettingsBeforeFailFast()", firstFailFast) < 0 ||
            dispatcherBody.LastIndexOf("FlushSettingsBeforeFailFast()", secondFailFast) < 0)
            throw new Exception("Each restart path must flush settings before FailFast, or the last debounced save is lost.");
        Console.WriteLine("PASS: ExitApp is idempotent, SessionEnding signals graceful shutdown, and both FailFast restart paths flush settings first.");

        // "vendor": third-party sources keep upstream layout so they stay diffable against upstream.
        var generatedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", ".claude", "bin", "obj", "artifacts", "worktrees", "checkpoints", "vendor" };
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
        RunLicenseWorkflowCheck(repositoryRoot);
    }

    // The license check must start for every change that can alter the dependency graph, and only for those.
    private static void RunLicenseWorkflowCheck(System.IO.DirectoryInfo repositoryRoot)
    {
        string root = repositoryRoot.FullName;
        string workflow = System.IO.File.ReadAllText(System.IO.Path.Combine(root, ".github", "workflows", "licenses.yml"));
        foreach (string filter in new[] { "'**/*.csproj'", "'**/*.props'", "'**/*.targets'", "'**/packages.lock.json'", "'.github/licenses/**'", "'.github/workflows/licenses.yml'" })
        {
            if (System.Text.RegularExpressions.Regex.Matches(workflow, System.Text.RegularExpressions.Regex.Escape(filter)).Count != 2)
                throw new Exception($"licenses.yml must list {filter} under both push and pull_request paths.");
        }
        if (!workflow.Contains("workflow_dispatch:") || workflow.Contains("schedule:"))
            throw new Exception("The license check runs on dependency changes or by hand, never on a schedule.");
        if (!System.IO.File.Exists(System.IO.Path.Combine(root, ".github", "licenses", "Check-Licenses.ps1")))
            throw new Exception("licenses.yml runs .github/licenses/Check-Licenses.ps1, which is missing.");

        // A package reference in a file type the path filter does not cover would change dependencies unnoticed.
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "artifacts", "worktrees", ".claude" };
        var uncovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".proj", ".projitems", ".fsproj", ".vbproj" };
        void Scan(System.IO.DirectoryInfo folder)
        {
            foreach (var entry in folder.EnumerateFileSystemInfos())
            {
                if (skipped.Contains(entry.Name) || (entry.Attributes & System.IO.FileAttributes.ReparsePoint) != 0) continue;
                if (entry is System.IO.DirectoryInfo child) Scan(child);
                else if (uncovered.Contains(entry.Extension) && System.IO.File.ReadAllText(entry.FullName).Contains("PackageReference"))
                    throw new Exception($"{entry.FullName} references packages but the license workflow's paths do not cover its file type.");
            }
        }
        Scan(repositoryRoot);

        using var policy = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, ".github", "licenses", "policy.json")));
        var allowed = policy.RootElement.GetProperty("allowedLicenses").EnumerateArray().Select(element => element.GetString()).ToArray();
        if (allowed.Length == 0 || allowed.Any(id => System.Text.RegularExpressions.Regex.IsMatch(id, "^(A?GPL|LGPL|SSPL|EUPL|CC-BY-NC)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new Exception("The license policy must allow some licenses and none of the copyleft or non-commercial ones.");
        foreach (var pin in policy.RootElement.GetProperty("licenseFileOverrides").EnumerateArray())
        {
            if (string.IsNullOrWhiteSpace(pin.GetProperty("id").GetString()) || string.IsNullOrWhiteSpace(pin.GetProperty("reason").GetString()) ||
                !allowed.Contains(pin.GetProperty("license").GetString(), StringComparer.OrdinalIgnoreCase) ||
                !System.Text.RegularExpressions.Regex.IsMatch(pin.GetProperty("licenseFileSha256").GetString(), "^[0-9A-Fa-f]{64}$"))
                throw new Exception($"A license file pin needs an id, an allowed license, a SHA-256 and a reason: {pin}");
        }
        Console.WriteLine("PASS: the license workflow starts on every dependency manifest change (push and pull request), never on a schedule, and the policy allows no copyleft license.");
    }
}
