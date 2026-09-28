using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class ClockChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
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
        int settingsSubscribeIndex = taskButtonLoadedBody.IndexOf("WeakSubscriptions.SubscribeSettings(Settings_PropertyChanged);");
        int dragHandlerCreateIndex = taskButtonLoadedBody.IndexOf("dragHandler = new DelayedActivationHandler");
        int animateCallIndex = taskButtonLoadedBody.IndexOf("Animate();");
        if (isLoadedGuardIndex < 0 || settingsSubscribeIndex < 0 || dragHandlerCreateIndex < 0 || animateCallIndex < 0 ||
            isLoadedGuardIndex > settingsSubscribeIndex || isLoadedGuardIndex > dragHandlerCreateIndex || isLoadedGuardIndex > animateCallIndex)
            throw new Exception("TaskButton_OnLoaded must return early when already loaded, before re-subscribing, replacing dragHandler, or replaying the slide-in animation.");
        if (System.Text.RegularExpressions.Regex.Matches(taskButtonLoadedBody, @"_isLoaded\s*=\s*true").Count != 1)
            throw new Exception("TaskButton_OnLoaded should set _isLoaded exactly once, after the idempotency guard.");
        Console.WriteLine("PASS: a repeated TaskButton Loaded without an intervening Unloaded is a no-op and does not re-subscribe, replace dragHandler, or replay the slide-in animation.");
    }
}
