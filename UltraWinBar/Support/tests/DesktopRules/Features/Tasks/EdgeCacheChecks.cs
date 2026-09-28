using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// Tasks_Filter runs for every window x every panel on each ListCollectionView Refresh, and used to
// re-read Settings.EnabledEdges (a fresh List allocation) and re-scan every TaskbarAssignment on
// each call. These checks pin: (1) EnabledEdges/Resolved*Edge are cached and invalidated only by
// the settings they actually depend on, and the cache is exposed read-only so callers can't
// mutate the shared instance; (2) the TaskbarAssignments-by-Identifier index used by
// GetAssignedEdge produces exactly the same result as the linear ResolveEdge scan.
internal static class EdgeCacheChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunEnabledEdgesCacheChecks();
        RunResolvedEdgeCacheChecks();
        RunIndexEquivalenceChecks();
    }

    private static void RunEnabledEdgesCacheChecks()
    {
        // EnabledEdges' declared type must be read-only, so a mutation attempt is a compile error
        // for every caller, not just a runtime convention.
        PropertyInfo enabledEdgesProperty = typeof(Settings).GetProperty(nameof(Settings.EnabledEdges));
        if (enabledEdgesProperty.PropertyType != typeof(IReadOnlyList<AppBarEdge>))
            throw new Exception("EnabledEdges must be declared as IReadOnlyList<AppBarEdge> so callers can't mutate the cached instance.");

        Settings.Instance.Edge = AppBarEdge.Right;
        Settings.Instance.AdditionalEdges = new List<AppBarEdge> { AppBarEdge.Left, AppBarEdge.Top, AppBarEdge.Bottom };

        IReadOnlyList<AppBarEdge> first = Settings.Instance.EnabledEdges;
        IReadOnlyList<AppBarEdge> second = Settings.Instance.EnabledEdges;
        if (!ReferenceEquals(first, second))
            throw new Exception("EnabledEdges must return the same cached instance when nothing relevant changed.");

        // Edge change invalidates the cache.
        Settings.Instance.Edge = AppBarEdge.Bottom;
        IReadOnlyList<AppBarEdge> afterEdgeChange = Settings.Instance.EnabledEdges;
        if (ReferenceEquals(first, afterEdgeChange) || !afterEdgeChange.Contains(AppBarEdge.Bottom))
            throw new Exception("EnabledEdges did not invalidate/refresh after Edge changed.");

        // AdditionalEdges change invalidates the cache too.
        Settings.Instance.AdditionalEdges = new List<AppBarEdge> { AppBarEdge.Right };
        IReadOnlyList<AppBarEdge> afterAdditionalChange = Settings.Instance.EnabledEdges;
        if (ReferenceEquals(afterEdgeChange, afterAdditionalChange) ||
            !afterAdditionalChange.Contains(AppBarEdge.Right) || afterAdditionalChange.Contains(AppBarEdge.Left))
            throw new Exception("EnabledEdges did not invalidate/refresh after AdditionalEdges changed.");

        // An unrelated property must not invalidate the cache.
        IReadOnlyList<AppBarEdge> beforeUnrelated = Settings.Instance.EnabledEdges;
        Settings.Instance.ShowClock = !Settings.Instance.ShowClock;
        IReadOnlyList<AppBarEdge> afterUnrelated = Settings.Instance.EnabledEdges;
        if (!ReferenceEquals(beforeUnrelated, afterUnrelated))
            throw new Exception("EnabledEdges must not invalidate its cache for an unrelated property change.");

        Console.WriteLine("PASS: EnabledEdges is cached, invalidated only by Edge/AdditionalEdges, and exposed as IReadOnlyList<AppBarEdge>.");
    }

    private static void RunResolvedEdgeCacheChecks()
    {
        Settings.Instance.Edge = AppBarEdge.Bottom;
        Settings.Instance.AdditionalEdges = new List<AppBarEdge> { AppBarEdge.Left, AppBarEdge.Top, AppBarEdge.Right };

        // TrayEdge not yet enabled anywhere -> falls back to Edge.
        Settings.Instance.TrayEdge = AppBarEdge.Bottom;
        if (Settings.Instance.ResolvedTrayEdge != AppBarEdge.Bottom)
            throw new Exception("ResolvedTrayEdge baseline mismatch.");

        // Changing TrayEdge to an already-enabled edge must refresh the cached Resolved value.
        Settings.Instance.TrayEdge = AppBarEdge.Left;
        if (Settings.Instance.ResolvedTrayEdge != AppBarEdge.Left)
            throw new Exception("ResolvedTrayEdge did not refresh after TrayEdge changed.");

        // Changing a different Resolved*Edge's own setting must not disturb ResolvedTrayEdge's cache.
        AppBarEdge trayBefore = Settings.Instance.ResolvedTrayEdge;
        Settings.Instance.ClockEdge = AppBarEdge.Top;
        if (Settings.Instance.ResolvedTrayEdge != trayBefore)
            throw new Exception("ResolvedTrayEdge must not change when an unrelated Resolved*Edge setting changes.");
        if (Settings.Instance.ResolvedClockEdge != AppBarEdge.Top)
            throw new Exception("ResolvedClockEdge did not refresh after ClockEdge changed.");

        // Removing TrayEdge's edge from AdditionalEdges must fall the resolved value back to Edge again.
        Settings.Instance.AdditionalEdges = new List<AppBarEdge> { AppBarEdge.Top, AppBarEdge.Right };
        if (Settings.Instance.ResolvedTrayEdge != Settings.Instance.Edge)
            throw new Exception("ResolvedTrayEdge did not fall back to Edge after its edge left AdditionalEdges.");

        Console.WriteLine("PASS: Resolved*Edge values are cached per-property and invalidated exactly by Edge/AdditionalEdges and their own *Edge setting.");
    }

    private static void RunIndexEquivalenceChecks()
    {
        MethodInfo indexedMethod = typeof(TaskAssignmentManager).GetMethod("ResolveEdgeIndexed", BindingFlags.NonPublic | BindingFlags.Static);
        if (indexedMethod == null) throw new Exception("TaskAssignmentManager.ResolveEdgeIndexed is missing.");

        AppBarEdge? Linear(List<TaskbarAssignment> assignments, Guid desktop, string windowId, string legacyWindowId, string appId) =>
            TaskAssignmentManager.ResolveEdge(assignments, desktop, windowId, legacyWindowId, appId);
        AppBarEdge? Indexed(List<TaskbarAssignment> assignments, Guid desktop, string windowId, string legacyWindowId, string appId) =>
            (AppBarEdge?)indexedMethod.Invoke(null, new object[] { assignments, desktop, windowId, legacyWindowId, appId });

        void AssertEqual(List<TaskbarAssignment> assignments, Guid desktop, string windowId, string legacyWindowId, string appId, string label)
        {
            AppBarEdge? expected = Linear(assignments, desktop, windowId, legacyWindowId, appId);
            AppBarEdge? actual = Indexed(assignments, desktop, windowId, legacyWindowId, appId);
            if (expected != actual)
                throw new Exception($"{label}: indexed lookup ({actual?.ToString() ?? "null"}) diverged from linear scan ({expected?.ToString() ?? "null"}).");
        }

        var desktopA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        var desktopB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

        // Fixed table: desktop scoping, duplicates/ties (later wins), legacy ids, nulls.
        var table = new List<TaskbarAssignment>
        {
            new() { Identifier = "exe:app", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Bottom },
            new() { Identifier = "exe:app", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Top, DesktopId = desktopA },
            new() { Identifier = "class:Win|title:T", Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Left, DesktopId = desktopA },
            new() { Identifier = "window:v2:1:2:3", Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Right, DesktopId = desktopA },
            // Same identifier+mode+scope repeated (duplicate): the later entry must win the tie.
            new() { Identifier = "window:v2:1:2:3", Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Bottom, DesktopId = desktopA },
            new() { Identifier = null, Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Top },
            new() { Identifier = "exe:app", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Right, DesktopId = desktopB },
        };
        AssertEqual(table, desktopA, "window:v2:1:2:3", "class:Win|title:T", "exe:app", "table/desktopA stable-window");
        AssertEqual(table, desktopA, "window:v2:9:9:9", "class:Win|title:T", "exe:app", "table/desktopA legacy fallback");
        AssertEqual(table, desktopB, "window:v2:1:2:3", "class:Win|title:T", "exe:app", "table/desktopB app scope");
        AssertEqual(table, Guid.NewGuid(), "window:v2:1:2:3", "class:Win|title:T", "exe:app", "table/unscoped desktop");
        AssertEqual(table, desktopA, null, null, null, "table/no identifiers");
        AssertEqual(table, desktopA, "nope", "nope", "nope", "table/no match");

        // Seeded random fuzz: many assignments (desktop-scoped/global mix, duplicate identifiers,
        // ties across modes) x many lookups, comparing the indexed path against the linear scan.
        var random = new Random(20260928);
        var desktops = new[] { Guid.Empty, desktopA, desktopB, Guid.NewGuid() };
        var identifiers = new[] { "exe:a", "exe:b", "class:X|title:Y", "window:v2:1:1:1", "window:v2:2:2:2", null };
        var modes = new[] { TaskAssignmentMode.ExecutablePath, TaskAssignmentMode.WindowClassAndTitle };
        var edges = new[] { AppBarEdge.Left, AppBarEdge.Top, AppBarEdge.Right, AppBarEdge.Bottom };

        var fuzzAssignments = new List<TaskbarAssignment>();
        for (int i = 0; i < 300; i++)
        {
            fuzzAssignments.Add(new TaskbarAssignment
            {
                Identifier = identifiers[random.Next(identifiers.Length)],
                Mode = modes[random.Next(modes.Length)],
                Edge = edges[random.Next(edges.Length)],
                DesktopId = desktops[random.Next(desktops.Length)],
            });
        }

        for (int i = 0; i < 200; i++)
        {
            Guid desktop = desktops[random.Next(desktops.Length)];
            string windowId = random.Next(3) == 0 ? null : identifiers[random.Next(identifiers.Length)];
            string legacyWindowId = random.Next(3) == 0 ? null : identifiers[random.Next(identifiers.Length)];
            string appId = random.Next(3) == 0 ? null : identifiers[random.Next(identifiers.Length)];
            AssertEqual(fuzzAssignments, desktop, windowId, legacyWindowId, appId, $"fuzz#{i}");
        }

        Console.WriteLine("PASS: TaskbarAssignments identifier index matches the linear ResolveEdge scan on a fixed table and 200 seeded-random lookups over 300 assignments.");
    }
}
