using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class PinChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
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
    }
}
