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
    }
}
