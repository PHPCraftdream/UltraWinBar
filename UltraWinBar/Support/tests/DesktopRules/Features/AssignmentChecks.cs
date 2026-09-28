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
    }
}
