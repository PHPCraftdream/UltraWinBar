using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class AssignmentChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        var desktopA = Guid.Parse("00000000-0000-0000-0000-000000000001");
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
        RunPinMoveChecks(desktopA, desktopB);
    }

    // Regression: a pinned application (Task Manager on the Right panel) could not be dragged to another panel:
    // a pin without a window ignored the drop, and for a running one only the window moved, so the pin stayed and
    // a click on it sent the application back. The pin and the application's rule move together.
    private static void RunPinMoveChecks(Guid desktopA, Guid desktopB)
    {
        PinnedApplication Pin(string identifier, AppBarEdge edge, Guid desktop) =>
            new() { Identifier = identifier, Edge = edge, DesktopId = desktop };

        var taskManager = Pin("exe:taskmgr", AppBarEdge.Right, desktopA);
        var sameAppOtherDesktop = Pin("exe:taskmgr", AppBarEdge.Right, desktopB);
        var otherApp = Pin("exe:browser", AppBarEdge.Right, desktopA);
        var moved = PinnedApplication.MoveToEdge(new[] { taskManager, sameAppOtherDesktop, otherApp }, taskManager, AppBarEdge.Bottom);
        if (moved.Count != 3 || taskManager.Edge != AppBarEdge.Bottom ||
            sameAppOtherDesktop.Edge != AppBarEdge.Right || otherApp.Edge != AppBarEdge.Right)
            throw new Exception("Moving a pin must change only that pin's panel, not the same application's pin on another desktop.");

        var onTarget = Pin("exe:taskmgr", AppBarEdge.Left, desktopA);
        var absorbed = PinnedApplication.MoveToEdge(new[] { taskManager, onTarget, otherApp }, taskManager, AppBarEdge.Left);
        if (absorbed.Count != 2 || absorbed.Contains(taskManager) || !absorbed.Contains(onTarget) || onTarget.Edge != AppBarEdge.Left)
            throw new Exception("A pin of the same application already on the target panel must absorb the moved one.");

        var rules = new List<TaskbarAssignment>
        {
            new() { Identifier = "exe:taskmgr", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Right, DesktopId = desktopA },
            new() { Identifier = "exe:taskmgr", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Right, DesktopId = desktopB },
            new() { Identifier = "exe:taskmgr", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Top },
            new() { Identifier = "exe:browser", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Right, DesktopId = desktopA },
            new() { Identifier = "exe:taskmgr", Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Left, DesktopId = desktopA },
        };
        var updated = TaskAssignmentManager.WithApplicationEdge(rules, desktopA, "exe:taskmgr", AppBarEdge.Bottom);
        if (updated.Count != 5 ||
            TaskAssignmentManager.ResolveEdge(updated, desktopA, null, "exe:taskmgr") != AppBarEdge.Bottom ||
            TaskAssignmentManager.ResolveEdge(updated, desktopB, null, "exe:taskmgr") != AppBarEdge.Right ||
            TaskAssignmentManager.ResolveEdge(updated, Guid.NewGuid(), null, "exe:taskmgr") != AppBarEdge.Top ||
            TaskAssignmentManager.ResolveEdge(updated, desktopA, null, "exe:browser") != AppBarEdge.Right)
            throw new Exception("The application's rule must be replaced for that desktop only, leaving other desktops, other applications and the all-desktops rule alone.");
        if (!updated.Any(rule => rule.Mode == TaskAssignmentMode.WindowClassAndTitle && rule.Edge == AppBarEdge.Left))
            throw new Exception("A per-window rule must survive moving the application.");
        Console.WriteLine("PASS: dragging a pinned application moves its pin and its rule together, for the current desktop only.");
    }
}
