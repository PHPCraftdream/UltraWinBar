using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// Task #127: differential test between TaskModel.Compute and an independently transcribed copy
// of the original TaskList.Pins.cs RebuildDisplayedTasks algorithm (adapted to TaskModel's pure
// snapshot inputs instead of ApplicationWindow/PinnedApplication/Settings). Mirrors the pattern
// already used for GroupByExecutableIdentifier/TaskListDiff.Reconcile in TaskGroupingAndDiffChecks:
// build both, compare on crafted scenarios covering every rule at once, then on seeded-random ones.
internal static class TaskModelEquivalenceChecks
{
    // Faithful re-transcription of RebuildDisplayedTasks's per-panel body, generalized to every
    // edge in one call. Deliberately written independently of TaskModel.cs's structure (inline
    // loops instead of shared private helpers) so a transcription slip in one is unlikely to be
    // mirrored in the other. Reuses TaskOrderIdentifier's pure helpers, same as TaskModel does —
    // those already have their own dedicated tests (TaskOrderChecks.cs).
    private static class ReferenceModel
    {
        internal static TaskModelResult Compute(TaskModelInput input)
        {
            var windows = input.Windows ?? Array.Empty<TaskWindowSnapshot>();
            var pins = input.Pins ?? Array.Empty<PinSnapshot>();
            var taskOrderIn = input.TaskOrder ?? Array.Empty<TaskOrderEntry>();
            var assignmentsIn = input.TaskbarAssignments ?? Array.Empty<TaskbarAssignment>();
            var windowStillExists = input.WindowStillExists ?? (_ => false);

            var liveKeys = new HashSet<string>();
            foreach (var w in windows) if (w.Key != null) liveKeys.Add(w.Key);
            var storedKeys = taskOrderIn.Select(e => e.Identifier)
                .Concat(assignmentsIn.Select(a => a.Identifier))
                .Concat(pins.Select(p => p.PrimaryWindowKey));
            foreach (var key in storedKeys)
                if (key != null && !liveKeys.Contains(key) && windowStillExists(key)) liveKeys.Add(key);

            var prunedOrder = TaskOrderIdentifier.PruneDeadWindowEntries(new List<TaskOrderEntry>(taskOrderIn), liveKeys);
            var prunedAssignments = TaskOrderIdentifier.PruneDeadWindowAssignments(new List<TaskbarAssignment>(assignmentsIn), liveKeys);
            var effectiveOrder = prunedOrder ?? new List<TaskOrderEntry>(taskOrderIn);

            var edgeResults = new Dictionary<AppBarEdge, TaskModelEdgeResult>();
            var allPinChanges = new List<PinPrimaryWindowKeyChange>();
            foreach (var edge in (input.EnabledEdges ?? Array.Empty<AppBarEdge>()).Distinct())
            {
                IReadOnlyDictionary<object, string> previous = null;
                input.PreviousDisplayKeys?.TryGetValue(edge, out previous);
                var (items, displayKeys, savedOrder, pinChanges) =
                    ComputeEdge(edge, windows, pins, effectiveOrder, input.CurrentDesktopId, liveKeys, previous ?? new Dictionary<object, string>());
                edgeResults[edge] = new TaskModelEdgeResult { Items = items, DisplayKeys = displayKeys, SavedOrder = savedOrder };
                allPinChanges.AddRange(pinChanges);
            }

            return new TaskModelResult
            {
                LiveKeys = liveKeys,
                PrunedTaskOrder = prunedOrder,
                PrunedTaskbarAssignments = prunedAssignments,
                PinPrimaryWindowKeyChanges = allPinChanges,
                Edges = edgeResults
            };
        }

        private static (List<object> Items, Dictionary<object, string> DisplayKeys, List<string> SavedOrder, List<PinPrimaryWindowKeyChange> PinChanges)
            ComputeEdge(AppBarEdge edge, IReadOnlyList<TaskWindowSnapshot> allWindows, IReadOnlyList<PinSnapshot> allPins,
                List<TaskOrderEntry> taskOrder, Guid desktopId, HashSet<string> liveKeys, IReadOnlyDictionary<object, string> previousDisplayKeys)
        {
            var windows = allWindows.Where(w => w.PassesFilter(edge)).ToList();

            var pins = new List<PinSnapshot>();
            var seenPinIds = new HashSet<string>();
            foreach (var pin in allPins)
                if (pin.Edge == edge && pin.OnCurrentDesktop && seenPinIds.Add(pin.Identifier)) pins.Add(pin);

            bool hasScopedOrder = false;
            foreach (var entry in taskOrder) if (entry.Edge == edge && entry.DesktopId == desktopId) { hasScopedOrder = true; break; }
            var order = new List<string>();
            foreach (var entry in taskOrder)
                if (entry.Edge == edge && entry.DesktopId == (hasScopedOrder ? desktopId : Guid.Empty)) order.Add(entry.Identifier);

            var orderSet = new HashSet<string>(order);
            var legacyIndex = new Dictionary<string, int>();
            for (int i = 0; i < order.Count; i++)
                if (order[i] != null && !legacyIndex.ContainsKey(order[i])) legacyIndex[order[i]] = i;

            foreach (var window in windows)
            {
                if (orderSet.Contains(window.Key)) continue;
                if (window.LegacyKey != null && legacyIndex.TryGetValue(window.LegacyKey, out int idx))
                {
                    orderSet.Remove(order[idx]);
                    order[idx] = window.Key;
                    orderSet.Add(window.Key);
                    legacyIndex.Remove(window.LegacyKey);
                    legacyIndex[window.Key] = idx;
                }
            }

            var byAppId = new Dictionary<string, List<TaskWindowSnapshot>>();
            foreach (var window in windows)
            {
                if (window.AppIdentifier == null) continue;
                if (!byAppId.TryGetValue(window.AppIdentifier, out var list)) byAppId[window.AppIdentifier] = list = new List<TaskWindowSnapshot>();
                list.Add(window);
            }

            var pinChanges = new List<PinPrimaryWindowKeyChange>();
            var keys = new Dictionary<object, string>();
            var claimed = new HashSet<TaskWindowSnapshot>();
            var items = new List<object>();

            foreach (var pin in pins)
            {
                var candidates = byAppId.TryGetValue(pin.Identifier, out var found) ? found : new List<TaskWindowSnapshot>();
                var group = new List<TaskWindowSnapshot>();
                foreach (var candidate in candidates) if (!claimed.Contains(candidate)) group.Add(candidate);

                TaskWindowSnapshot chosen = null;
                foreach (var candidate in group) if (candidate.Key == pin.PrimaryWindowKey) { chosen = candidate; break; }
                if (chosen == null)
                    foreach (var candidate in group)
                        if (previousDisplayKeys.TryGetValue(candidate.Window, out string previousKey) && previousKey == pin.OrderKey) { chosen = candidate; break; }
                if (chosen == null)
                    foreach (var candidate in group)
                        if (!orderSet.Contains(candidate.Key)) { chosen = candidate; break; }
                if (chosen == null && group.Count > 0) chosen = group[0];

                if (chosen != null)
                {
                    string reconciled = TaskOrderIdentifier.ReconcilePrimaryWindowKey(pin.PrimaryWindowKey, chosen.Key, liveKeys);
                    if (reconciled != pin.PrimaryWindowKey)
                        pinChanges.Add(new PinPrimaryWindowKeyChange { Pin = pin.Pin, NewPrimaryWindowKey = reconciled });
                }

                object item = chosen != null ? chosen.Window : pin.Pin;
                foreach (var candidate in group) claimed.Add(candidate);
                items.Add(item);
                keys[item] = pin.OrderKey;
                foreach (var candidate in group)
                {
                    if (ReferenceEquals(candidate, chosen)) continue;
                    items.Add(candidate.Window);
                    keys[candidate.Window] = candidate.Key ?? candidate.FallbackKey;
                }
            }
            foreach (var window in windows)
            {
                if (claimed.Contains(window)) continue;
                items.Add(window.Window);
                keys[window.Window] = window.Key ?? window.FallbackKey;
            }

            var orderIndexes = new Dictionary<string, int>();
            int nullIndex = -1;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == null) { if (nullIndex < 0) nullIndex = i; }
                else if (!orderIndexes.ContainsKey(order[i])) orderIndexes[order[i]] = i;
            }
            var sorted = items.Select((item, position) =>
            {
                string key = keys[item];
                int index = key == null ? nullIndex : (orderIndexes.TryGetValue(key, out int found2) ? found2 : -1);
                return (item, rank: index < 0 ? int.MaxValue : index, position);
            }).OrderBy(x => x.rank).ThenBy(x => x.position).Select(x => x.item).ToList();

            foreach (var item in sorted)
                if (orderSet.Add(keys[item])) order.Add(keys[item]);

            return (sorted, keys, order, pinChanges);
        }
    }

    private static void AssertEqual(TaskModelResult expected, TaskModelResult actual, string context)
    {
        if (!expected.LiveKeys.SetEquals(actual.LiveKeys))
            throw new Exception($"{context}: LiveKeys diverged.");
        bool PruneOrderEqual() => (expected.PrunedTaskOrder, actual.PrunedTaskOrder) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            var (e, a) => e.SequenceEqual(a)
        };
        if (!PruneOrderEqual()) throw new Exception($"{context}: PrunedTaskOrder diverged.");
        bool PruneAssignmentsEqual() => (expected.PrunedTaskbarAssignments, actual.PrunedTaskbarAssignments) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            var (e, a) => e.SequenceEqual(a)
        };
        if (!PruneAssignmentsEqual()) throw new Exception($"{context}: PrunedTaskbarAssignments diverged.");
        var expectedChanges = expected.PinPrimaryWindowKeyChanges.Select(c => (c.Pin, c.NewPrimaryWindowKey)).ToList();
        var actualChanges = actual.PinPrimaryWindowKeyChanges.Select(c => (c.Pin, c.NewPrimaryWindowKey)).ToList();
        if (expectedChanges.Count != actualChanges.Count ||
            !expectedChanges.OrderBy(c => c.Pin?.ToString()).SequenceEqual(actualChanges.OrderBy(c => c.Pin?.ToString())))
            throw new Exception($"{context}: PinPrimaryWindowKeyChanges diverged.");
        if (!expected.Edges.Keys.ToHashSet().SetEquals(actual.Edges.Keys))
            throw new Exception($"{context}: the set of computed edges diverged.");
        foreach (var edge in expected.Edges.Keys)
        {
            var e = expected.Edges[edge];
            var a = actual.Edges[edge];
            if (!e.Items.SequenceEqual(a.Items))
                throw new Exception($"{context}/{edge}: Items diverged.");
            if (e.DisplayKeys.Count != a.DisplayKeys.Count || e.DisplayKeys.Any(kv => !a.DisplayKeys.TryGetValue(kv.Key, out string v) || v != kv.Value))
                throw new Exception($"{context}/{edge}: DisplayKeys diverged.");
            if (!e.SavedOrder.SequenceEqual(a.SavedOrder))
                throw new Exception($"{context}/{edge}: SavedOrder diverged.");
        }
    }

    private static void Compare(TaskModelInput input, string context)
    {
        AssertEqual(ReferenceModel.Compute(input), TaskModel.Compute(input), context);
    }

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunCraftedScenarios();
        RunRandomizedScenarios();
    }

    private static void RunCraftedScenarios()
    {
        const AppBarEdge bottom = AppBarEdge.Bottom, top = AppBarEdge.Top;
        var desktopA = Guid.Parse("00000000-0000-0000-0000-0000000000AA");

        // Everything at once: saved order, a brand-new window, legacy migration, a pinned app
        // with several windows (one chosen by PrimaryWindowKey), a solo/orphan pin, a dead
        // TaskOrder entry, a dead TaskbarAssignment, and two edges splitting the window set.
        var w1 = new TaskWindowSnapshot { Window = "w1", Key = "window:v2:w1", AppIdentifier = "appA", PassesFilter = e => e == bottom };
        var w2 = new TaskWindowSnapshot { Window = "w2", Key = "window:v2:w2", AppIdentifier = "appA", PassesFilter = e => e == bottom };
        var w3 = new TaskWindowSnapshot { Window = "w3", Key = "window:v2:w3new", LegacyKey = "class:Old|title:W3", AppIdentifier = "appB", PassesFilter = e => e == bottom };
        var w4 = new TaskWindowSnapshot { Window = "w4", Key = "window:v2:w4", AppIdentifier = "appC", PassesFilter = e => e == top };
        var pinAppA = new PinSnapshot { Pin = "pinA", Edge = bottom, Identifier = "appA", PrimaryWindowKey = "window:v2:w2", OrderKey = "pin:appA", OnCurrentDesktop = true };
        var pinOrphan = new PinSnapshot { Pin = "pinOrphan", Edge = bottom, Identifier = "appZ", PrimaryWindowKey = "window:v2:dead", OrderKey = "pin:appZ", OnCurrentDesktop = true };
        var taskOrder = new List<TaskOrderEntry>
        {
            new() { Edge = bottom, Identifier = "pin:appA" },
            new() { Edge = bottom, Identifier = "class:Old|title:W3" },
            new() { Edge = bottom, Identifier = "window:v2:deadOnly" },
            new() { Edge = top, Identifier = "window:v2:w4" },
        };
        var assignments = new List<TaskbarAssignment>
        {
            new() { Mode = TaskAssignmentMode.WindowClassAndTitle, Identifier = "window:v2:w1", Edge = bottom },
            new() { Mode = TaskAssignmentMode.WindowClassAndTitle, Identifier = "window:v2:deadAssignment", Edge = bottom },
        };
        var craftedInput = new TaskModelInput
        {
            Windows = new[] { w1, w2, w3, w4 },
            Pins = new[] { pinAppA, pinOrphan },
            TaskOrder = taskOrder,
            TaskbarAssignments = assignments,
            EnabledEdges = new[] { bottom, top },
            CurrentDesktopId = desktopA,
        };
        Compare(craftedInput, "Crafted: saved order + new window + legacy migration + pin selection/orphan + pruning + two edges");

        // Idempotence round-trip through both implementations: apply TaskModel's own SavedOrder
        // back and re-run both; they must still agree (and agree with themselves).
        var first = TaskModel.Compute(craftedInput);
        var rewrittenOrder = TaskModel.ApplyOrderForEdge(first.PrunedTaskOrder ?? taskOrder, bottom, desktopA, first.Edges[bottom].SavedOrder);
        rewrittenOrder = TaskModel.ApplyOrderForEdge(rewrittenOrder, top, desktopA, first.Edges[top].SavedOrder);
        var previous = new Dictionary<AppBarEdge, IReadOnlyDictionary<object, string>>
        {
            [bottom] = first.Edges[bottom].DisplayKeys,
            [top] = first.Edges[top].DisplayKeys,
        };
        var secondInput = new TaskModelInput
        {
            Windows = craftedInput.Windows,
            Pins = craftedInput.Pins,
            TaskOrder = rewrittenOrder,
            TaskbarAssignments = first.PrunedTaskbarAssignments ?? assignments,
            EnabledEdges = craftedInput.EnabledEdges,
            CurrentDesktopId = desktopA,
            PreviousDisplayKeys = previous,
        };
        Compare(secondInput, "Crafted round 2 (fed back through TaskModel's own SavedOrder/DisplayKeys)");
        Console.WriteLine("PASS: TaskModel.Compute matches an independently transcribed reference implementation on a crafted scenario exercising every rule at once, across two rounds.");
    }

    private static void RunRandomizedScenarios()
    {
        var random = new Random(127127);
        var edges = new[] { AppBarEdge.Bottom, AppBarEdge.Top, AppBarEdge.Left };
        var desktops = new[] { Guid.Empty, Guid.Parse("00000000-0000-0000-0000-000000000D01"), Guid.Parse("00000000-0000-0000-0000-000000000D02") };

        for (int trial = 0; trial < 200; trial++)
        {
            int windowCount = random.Next(0, 14);
            var windows = new List<TaskWindowSnapshot>();
            var windowKeys = new List<string>();
            for (int i = 0; i < windowCount; i++)
            {
                string key = $"window:v2:{trial}:{i}";
                windowKeys.Add(key);
                string legacyKey = random.Next(4) == 0 ? $"class:Legacy{random.Next(4)}|title:T{trial}" : null;
                string appId = random.Next(6) == 0 ? null : $"app{random.Next(4)}";
                var passesEdges = edges.Where(_ => random.Next(2) == 0).ToArray();
                if (passesEdges.Length == 0) passesEdges = new[] { edges[random.Next(edges.Length)] };
                var allowed = new HashSet<AppBarEdge>(passesEdges);
                windows.Add(new TaskWindowSnapshot
                {
                    Window = $"w{trial}_{i}",
                    Key = key,
                    LegacyKey = legacyKey,
                    FallbackKey = "hwnd:" + i,
                    AppIdentifier = appId,
                    PassesFilter = allowed.Contains
                });
            }

            int pinCount = random.Next(0, 5);
            var pins = new List<PinSnapshot>();
            for (int i = 0; i < pinCount; i++)
            {
                string identifier = $"app{random.Next(4)}";
                string primary = random.Next(3) switch
                {
                    0 => windowKeys.Count > 0 ? windowKeys[random.Next(windowKeys.Count)] : null,
                    1 => $"window:v2:phantom{random.Next(3)}",
                    _ => null
                };
                pins.Add(new PinSnapshot
                {
                    Pin = $"pin{trial}_{i}",
                    Edge = edges[random.Next(edges.Length)],
                    Identifier = identifier,
                    PrimaryWindowKey = primary,
                    OrderKey = "pin:" + identifier,
                    OnCurrentDesktop = random.Next(5) != 0
                });
            }

            int orderCount = random.Next(0, 12);
            var taskOrder = new List<TaskOrderEntry>();
            var orderKeyPool = windowKeys
                .Concat(pins.Select(p => p.OrderKey))
                .Concat(new[] { "class:Legacy0|title:T" + trial, "class:Legacy1|title:T" + trial, "class:Legacy2|title:T" + trial, "class:Legacy3|title:T" + trial })
                .Concat(Enumerable.Range(0, 3).Select(i => $"window:v2:gone{trial}:{i}"))
                .Append((string)null)
                .ToList();
            for (int i = 0; i < orderCount; i++)
                taskOrder.Add(new TaskOrderEntry
                {
                    Edge = edges[random.Next(edges.Length)],
                    DesktopId = desktops[random.Next(desktops.Length)],
                    Identifier = orderKeyPool[random.Next(orderKeyPool.Count)]
                });

            int assignmentCount = random.Next(0, 6);
            var assignments = new List<TaskbarAssignment>();
            for (int i = 0; i < assignmentCount; i++)
                assignments.Add(new TaskbarAssignment
                {
                    Mode = random.Next(2) == 0 ? TaskAssignmentMode.WindowClassAndTitle : TaskAssignmentMode.ExecutablePath,
                    Identifier = orderKeyPool[random.Next(orderKeyPool.Count)],
                    Edge = edges[random.Next(edges.Length)],
                    DesktopId = desktops[random.Next(desktops.Length)]
                });

            var phantomAlive = new HashSet<string>(Enumerable.Range(0, 3)
                .Where(_ => random.Next(2) == 0)
                .Select(i => $"window:v2:phantom{i}"));
            bool WindowStillExists(string key) => phantomAlive.Contains(key);

            IReadOnlyDictionary<AppBarEdge, IReadOnlyDictionary<object, string>> previous = null;
            if (random.Next(2) == 0 && windows.Count > 0)
            {
                var map = new Dictionary<object, string>();
                foreach (var w in windows.Where(_ => random.Next(2) == 0))
                    map[w.Window] = random.Next(2) == 0 ? w.Key : pins.Count > 0 ? pins[random.Next(pins.Count)].OrderKey : w.Key;
                previous = new Dictionary<AppBarEdge, IReadOnlyDictionary<object, string>> { [edges[random.Next(edges.Length)]] = map };
            }

            var input = new TaskModelInput
            {
                Windows = windows,
                Pins = pins,
                TaskOrder = taskOrder,
                TaskbarAssignments = assignments,
                EnabledEdges = edges,
                CurrentDesktopId = desktops[random.Next(desktops.Length)],
                WindowStillExists = WindowStillExists,
                PreviousDisplayKeys = previous
            };
            Compare(input, $"Randomized trial {trial}");
        }
        Console.WriteLine("PASS: TaskModel.Compute matches the reference implementation across 200 randomized scenarios covering ordering, legacy migration, pin selection/reconciliation, pruning, multi-edge splits, and desktop scoping.");
    }
}
