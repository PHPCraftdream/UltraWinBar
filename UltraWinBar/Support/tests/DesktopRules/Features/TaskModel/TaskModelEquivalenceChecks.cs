using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// Task #127/#128: differential test between TaskModel.Compute and an independently transcribed
// reference implementation. Covers both single-panel-per-edge scenarios (#127) and #128's
// multi-panel-same-edge chaining: several panels (e.g. one per monitor under Settings.ShowMultiMon)
// registered on one edge, each with its own Filter and PreviousDisplayKeys, processed in list order
// and seeing the previous same-edge panel's order/pin reconciliation — equivalent to the old
// sequential per-panel RebuildDisplayedTasks + SetTaskOrderForEdge calls, which mutated the shared
// TaskOrder/PinnedApplication state directly between panels.
internal static class TaskModelEquivalenceChecks
{
    // Faithful, independently structured re-transcription of TaskModel's algorithm (inline loops,
    // a mutable "current primary key"/"current edge order" dictionary threaded across panels,
    // instead of TaskModel.cs's own private helpers), so a transcription slip in one is unlikely to
    // be mirrored in the other. Reuses TaskOrderIdentifier's pure helpers, same as TaskModel does —
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

            // Each pin's live PrimaryWindowKey, mutated as panels on its edge reconcile it in turn —
            // mirrors the old code mutating the shared PinnedApplication instance directly.
            var currentPrimary = new Dictionary<object, string>();
            foreach (var p in pins) currentPrimary[p.Pin] = p.PrimaryWindowKey;

            // Each edge's running order, seeded on first use from the (already pruned) TaskOrder,
            // then replaced by whichever panel on that edge ran last.
            var edgeOrder = new Dictionary<AppBarEdge, List<string>>();
            var panelResults = new Dictionary<object, TaskModelEdgeResult>();

            foreach (var panel in input.Panels ?? Array.Empty<TaskPanelRequest>())
            {
                if (!edgeOrder.TryGetValue(panel.Edge, out var orderSeed))
                {
                    bool scoped = effectiveOrder.Any(e => e.Edge == panel.Edge && e.DesktopId == input.CurrentDesktopId);
                    var scope = scoped ? input.CurrentDesktopId : Guid.Empty;
                    orderSeed = effectiveOrder.Where(e => e.Edge == panel.Edge && e.DesktopId == scope).Select(e => e.Identifier).ToList();
                }

                var (items, keys, savedOrder) = ComputePanel(panel, orderSeed, windows, pins, currentPrimary, liveKeys);
                edgeOrder[panel.Edge] = savedOrder;
                panelResults[panel.PanelId] = new TaskModelEdgeResult { Items = items, DisplayKeys = keys, SavedOrder = savedOrder };
            }

            var pinChanges = new List<PinPrimaryWindowKeyChange>();
            foreach (var p in pins)
                if (currentPrimary[p.Pin] != p.PrimaryWindowKey)
                    pinChanges.Add(new PinPrimaryWindowKeyChange { Pin = p.Pin, NewPrimaryWindowKey = currentPrimary[p.Pin] });

            return new TaskModelResult
            {
                LiveKeys = liveKeys,
                PrunedTaskOrder = prunedOrder,
                PrunedTaskbarAssignments = prunedAssignments,
                PinPrimaryWindowKeyChanges = pinChanges,
                Panels = panelResults,
                SavedOrders = edgeOrder
            };
        }

        private static (List<object> Items, Dictionary<object, string> DisplayKeys, List<string> SavedOrder) ComputePanel(
            TaskPanelRequest panel, List<string> orderSeed, IReadOnlyList<TaskWindowSnapshot> allWindows,
            IReadOnlyList<PinSnapshot> allPins, Dictionary<object, string> currentPrimary, HashSet<string> liveKeys)
        {
            var previousDisplayKeys = panel.PreviousDisplayKeys ?? new Dictionary<object, string>();
            var windows = allWindows.Where(w => panel.Filter != null && panel.Filter(w.Window)).ToList();

            var pins = new List<PinSnapshot>();
            var seenPinIds = new HashSet<string>();
            foreach (var pin in allPins)
                if (pin.Edge == panel.Edge && pin.OnCurrentDesktop && seenPinIds.Add(pin.Identifier)) pins.Add(pin);

            var order = new List<string>(orderSeed);
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

            var keys = new Dictionary<object, string>();
            var claimed = new HashSet<TaskWindowSnapshot>();
            var items = new List<object>();

            foreach (var pin in pins)
            {
                var candidates = byAppId.TryGetValue(pin.Identifier, out var found) ? found : new List<TaskWindowSnapshot>();
                var group = new List<TaskWindowSnapshot>();
                foreach (var candidate in candidates) if (!claimed.Contains(candidate)) group.Add(candidate);

                string effectivePrimary = currentPrimary.TryGetValue(pin.Pin, out var overridden) ? overridden : pin.PrimaryWindowKey;

                TaskWindowSnapshot chosen = null;
                foreach (var candidate in group) if (candidate.Key == effectivePrimary) { chosen = candidate; break; }
                if (chosen == null)
                    foreach (var candidate in group)
                        if (previousDisplayKeys.TryGetValue(candidate.Window, out string previousKey) && previousKey == pin.OrderKey) { chosen = candidate; break; }
                if (chosen == null)
                    foreach (var candidate in group)
                        if (!orderSet.Contains(candidate.Key)) { chosen = candidate; break; }
                if (chosen == null && group.Count > 0) chosen = group[0];

                if (chosen != null)
                {
                    string reconciled = TaskOrderIdentifier.ReconcilePrimaryWindowKey(effectivePrimary, chosen.Key, liveKeys);
                    if (reconciled != effectivePrimary) currentPrimary[pin.Pin] = reconciled;
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

            return (sorted, keys, order);
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
        if (!expected.Panels.Keys.ToHashSet().SetEquals(actual.Panels.Keys))
            throw new Exception($"{context}: the set of computed panels diverged.");
        foreach (var panelId in expected.Panels.Keys)
        {
            var e = expected.Panels[panelId];
            var a = actual.Panels[panelId];
            if (!e.Items.SequenceEqual(a.Items))
                throw new Exception($"{context}/{panelId}: Items diverged.");
            if (e.DisplayKeys.Count != a.DisplayKeys.Count || e.DisplayKeys.Any(kv => !a.DisplayKeys.TryGetValue(kv.Key, out string v) || v != kv.Value))
                throw new Exception($"{context}/{panelId}: DisplayKeys diverged.");
            if (!e.SavedOrder.SequenceEqual(a.SavedOrder))
                throw new Exception($"{context}/{panelId}: SavedOrder diverged.");
        }
        if (!expected.SavedOrders.Keys.ToHashSet().SetEquals(actual.SavedOrders.Keys))
            throw new Exception($"{context}: the set of edges with a final SavedOrders entry diverged.");
        foreach (var edge in expected.SavedOrders.Keys)
            if (!expected.SavedOrders[edge].SequenceEqual(actual.SavedOrders[edge]))
                throw new Exception($"{context}/{edge}: final SavedOrders diverged.");
    }

    private static void Compare(TaskModelInput input, string context)
    {
        AssertEqual(ReferenceModel.Compute(input), TaskModel.Compute(input), context);
    }

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunCraftedScenarios();
        RunRandomizedScenarios();
        RunMultiPanelSameEdgeScenarios();
    }

    private static TaskWindowSnapshot Win(object id, string key, string legacyKey = null, string appId = null, string fallback = null) =>
        new() { Window = id, Key = key, LegacyKey = legacyKey, AppIdentifier = appId, FallbackKey = fallback ?? "hwnd:" + id };

    private static void RunCraftedScenarios()
    {
        const AppBarEdge bottom = AppBarEdge.Bottom, top = AppBarEdge.Top;
        var desktopA = Guid.Parse("00000000-0000-0000-0000-0000000000AA");

        // Everything at once: saved order, a brand-new window, legacy migration, a pinned app
        // with several windows (one chosen by PrimaryWindowKey), a solo/orphan pin, a dead
        // TaskOrder entry, a dead TaskbarAssignment, and two edges splitting the window set.
        var w1 = Win("w1", "window:v2:w1", appId: "appA");
        var w2 = Win("w2", "window:v2:w2", appId: "appA");
        var w3 = Win("w3", "window:v2:w3new", legacyKey: "class:Old|title:W3", appId: "appB");
        var w4 = Win("w4", "window:v2:w4", appId: "appC");
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
        bool BottomFilter(object w) => Equals(w, "w1") || Equals(w, "w2") || Equals(w, "w3");
        bool TopFilter(object w) => Equals(w, "w4");
        var panels = new[]
        {
            new TaskPanelRequest { PanelId = bottom, Edge = bottom, Filter = BottomFilter },
            new TaskPanelRequest { PanelId = top, Edge = top, Filter = TopFilter },
        };
        var craftedInput = new TaskModelInput
        {
            Windows = new[] { w1, w2, w3, w4 },
            Pins = new[] { pinAppA, pinOrphan },
            TaskOrder = taskOrder,
            TaskbarAssignments = assignments,
            Panels = panels,
            CurrentDesktopId = desktopA,
        };
        Compare(craftedInput, "Crafted: saved order + new window + legacy migration + pin selection/orphan + pruning + two edges");

        // Idempotence round-trip: apply the model's own final per-edge SavedOrders back and
        // re-run; both implementations must still agree (and agree with themselves).
        var first = TaskModel.Compute(craftedInput);
        var rewrittenOrder = TaskModel.ApplyOrderForEdge(first.PrunedTaskOrder ?? taskOrder, bottom, desktopA, first.SavedOrders[bottom]);
        rewrittenOrder = TaskModel.ApplyOrderForEdge(rewrittenOrder, top, desktopA, first.SavedOrders[top]);
        var panelsRound2 = new[]
        {
            new TaskPanelRequest { PanelId = bottom, Edge = bottom, Filter = BottomFilter, PreviousDisplayKeys = first.Panels[bottom].DisplayKeys },
            new TaskPanelRequest { PanelId = top, Edge = top, Filter = TopFilter, PreviousDisplayKeys = first.Panels[top].DisplayKeys },
        };
        var secondInput = new TaskModelInput
        {
            Windows = craftedInput.Windows,
            Pins = craftedInput.Pins,
            TaskOrder = rewrittenOrder,
            TaskbarAssignments = first.PrunedTaskbarAssignments ?? assignments,
            Panels = panelsRound2,
            CurrentDesktopId = desktopA,
        };
        Compare(secondInput, "Crafted round 2 (fed back through TaskModel's own SavedOrders/DisplayKeys)");
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
            var allowedEdgesByWindow = new Dictionary<object, HashSet<AppBarEdge>>();
            for (int i = 0; i < windowCount; i++)
            {
                string key = $"window:v2:{trial}:{i}";
                windowKeys.Add(key);
                string legacyKey = random.Next(4) == 0 ? $"class:Legacy{random.Next(4)}|title:T{trial}" : null;
                string appId = random.Next(6) == 0 ? null : $"app{random.Next(4)}";
                var passesEdges = edges.Where(_ => random.Next(2) == 0).ToArray();
                if (passesEdges.Length == 0) passesEdges = new[] { edges[random.Next(edges.Length)] };
                object id = $"w{trial}_{i}";
                allowedEdgesByWindow[id] = new HashSet<AppBarEdge>(passesEdges);
                windows.Add(new TaskWindowSnapshot
                {
                    Window = id,
                    Key = key,
                    LegacyKey = legacyKey,
                    FallbackKey = "hwnd:" + i,
                    AppIdentifier = appId,
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

            // One panel per edge — this trial exercises the single-panel-per-edge path; multi-panel
            // same-edge chaining is covered separately in RunMultiPanelSameEdgeScenarios.
            var panels = edges.Select(edge =>
            {
                IReadOnlyDictionary<object, string> previous = null;
                if (random.Next(2) == 0 && windows.Count > 0)
                {
                    var map = new Dictionary<object, string>();
                    foreach (var w in windows.Where(_ => random.Next(2) == 0))
                        map[w.Window] = random.Next(2) == 0 ? w.Key : pins.Count > 0 ? pins[random.Next(pins.Count)].OrderKey : w.Key;
                    previous = map;
                }
                return new TaskPanelRequest { PanelId = edge, Edge = edge, Filter = w => allowedEdgesByWindow.TryGetValue(w, out var set) && set.Contains(edge), PreviousDisplayKeys = previous };
            }).ToList();

            var input = new TaskModelInput
            {
                Windows = windows,
                Pins = pins,
                TaskOrder = taskOrder,
                TaskbarAssignments = assignments,
                Panels = panels,
                CurrentDesktopId = desktops[random.Next(desktops.Length)],
                WindowStillExists = WindowStillExists,
            };
            Compare(input, $"Randomized trial {trial}");
        }
        Console.WriteLine("PASS: TaskModel.Compute matches the reference implementation across 200 randomized scenarios covering ordering, legacy migration, pin selection/reconciliation, pruning, multi-edge splits, and desktop scoping.");
    }

    private static void RunMultiPanelSameEdgeScenarios()
    {
        const AppBarEdge edge = AppBarEdge.Bottom;

        // Crafted: two panels (two monitors) on the same edge. Panel 1 sees only windows m1/shared;
        // panel 2 sees only m2/shared. A pin for "app" has candidates on both monitors: panel 1
        // reconciles the pin to its own monitor's window first; panel 2 must see that already-
        // reconciled PrimaryWindowKey (not the original), matching the old sequential per-panel
        // mutation of the shared PinnedApplication instance.
        var m1 = Win("m1", "window:v2:m1", appId: "app");
        var m2 = Win("m2", "window:v2:m2", appId: "app");
        var pin = new PinSnapshot { Pin = "pinApp", Edge = edge, Identifier = "app", PrimaryWindowKey = "window:v2:dead", OrderKey = "pin:app", OnCurrentDesktop = true };
        var panel1 = new TaskPanelRequest { PanelId = "monitor1", Edge = edge, Filter = w => Equals(w, "m1") };
        var panel2 = new TaskPanelRequest { PanelId = "monitor2", Edge = edge, Filter = w => Equals(w, "m2") };
        var chainedInput = new TaskModelInput
        {
            Windows = new[] { m1, m2 },
            Pins = new[] { pin },
            Panels = new[] { panel1, panel2 },
        };
        Compare(chainedInput, "Multi-panel same edge: pin reconciliation chains from the first panel to the second");

        var chainedResult = TaskModel.Compute(chainedInput);
        var change = chainedResult.PinPrimaryWindowKeyChanges.SingleOrDefault(c => Equals(c.Pin, "pinApp"));
        if (change.Pin == null || change.NewPrimaryWindowKey != "window:v2:m1")
            throw new Exception("Panel 1 (processed first) must claim the pin's representative, since it is the only monitor with a live candidate at that point.");
        Console.WriteLine("PASS: a pin's PrimaryWindowKey reconciled by the first same-edge panel is visible (final) even though a second panel on the same edge also has a candidate.");

        // Crafted: order chaining. Panel 1 discovers a brand-new window and appends its key;
        // panel 2 on the same edge must start from panel 1's SavedOrder (see the appended key),
        // not re-read the original TaskOrder — exactly like sequential SetTaskOrderForEdge calls.
        var n1 = Win("n1", "window:v2:n1");
        var n2 = Win("n2", "window:v2:n2");
        var orderPanels = new[]
        {
            new TaskPanelRequest { PanelId = "p1", Edge = edge, Filter = w => Equals(w, "n1") },
            new TaskPanelRequest { PanelId = "p2", Edge = edge, Filter = w => Equals(w, "n1") || Equals(w, "n2") },
        };
        var orderInput = new TaskModelInput { Windows = new[] { n1, n2 }, Panels = orderPanels };
        Compare(orderInput, "Multi-panel same edge: order chaining (second panel starts from the first panel's SavedOrder)");
        var orderResult = TaskModel.Compute(orderInput);
        if (!orderResult.Panels["p2"].SavedOrder.SequenceEqual(new[] { "window:v2:n1", "window:v2:n2" }))
            throw new Exception("The second same-edge panel must start from the first panel's SavedOrder, so n1 (discovered first) keeps its earlier slot.");
        if (!orderResult.SavedOrders[edge].SequenceEqual(orderResult.Panels["p2"].SavedOrder))
            throw new Exception("The edge's final SavedOrders entry must equal the last same-edge panel's SavedOrder.");
        Console.WriteLine("PASS: same-edge panels chain their order — the last panel's SavedOrder is the edge's final persisted order.");

        // Randomized: 2-3 panels per edge (multi-monitor), each with a random window/edge subset,
        // against the reference implementation's independent chaining.
        var random = new Random(128128);
        var edges = new[] { AppBarEdge.Bottom, AppBarEdge.Top };
        for (int trial = 0; trial < 150; trial++)
        {
            int windowCount = random.Next(0, 10);
            var windows = new List<TaskWindowSnapshot>();
            var windowIds = new List<object>();
            for (int i = 0; i < windowCount; i++)
            {
                object id = $"w{trial}_{i}";
                windowIds.Add(id);
                windows.Add(new TaskWindowSnapshot
                {
                    Window = id,
                    Key = $"window:v2:{trial}:{i}",
                    LegacyKey = random.Next(5) == 0 ? $"class:L{random.Next(3)}|title:T{trial}" : null,
                    FallbackKey = "hwnd:" + i,
                    AppIdentifier = random.Next(4) == 0 ? null : $"app{random.Next(3)}",
                });
            }

            int pinCount = random.Next(0, 4);
            var pins = new List<PinSnapshot>();
            for (int i = 0; i < pinCount; i++)
            {
                string identifier = $"app{random.Next(3)}";
                pins.Add(new PinSnapshot
                {
                    Pin = $"pin{trial}_{i}",
                    Edge = edges[random.Next(edges.Length)],
                    Identifier = identifier,
                    PrimaryWindowKey = random.Next(2) == 0 && windowIds.Count > 0 ? windows[random.Next(windows.Count)].Key : $"window:v2:phantom{trial}_{i}",
                    OrderKey = "pin:" + identifier,
                    OnCurrentDesktop = random.Next(5) != 0
                });
            }

            var taskOrder = new List<TaskOrderEntry>();
            var orderPool = windows.Select(w => w.Key).Concat(pins.Select(p => p.OrderKey)).Append((string)null).ToList();
            for (int i = 0; i < random.Next(0, 8); i++)
                taskOrder.Add(new TaskOrderEntry { Edge = edges[random.Next(edges.Length)], Identifier = orderPool[random.Next(orderPool.Count)] });

            // 2-3 panels per edge, each assigned a random subset of that edge's windows (so panels
            // on the same edge frequently overlap or diverge, the scenario #128 targets).
            var panels = new List<TaskPanelRequest>();
            foreach (var e in edges)
            {
                int panelCount = 1 + random.Next(3);
                for (int p = 0; p < panelCount; p++)
                {
                    var visible = new HashSet<object>(windowIds.Where(_ => random.Next(2) == 0));
                    IReadOnlyDictionary<object, string> previous = null;
                    if (random.Next(2) == 0)
                    {
                        var map = new Dictionary<object, string>();
                        foreach (var id in windowIds.Where(_ => random.Next(3) == 0))
                            map[id] = random.Next(2) == 0 ? windows.First(w => Equals(w.Window, id)).Key
                                : pins.Count > 0 ? pins[random.Next(pins.Count)].OrderKey : null;
                        previous = map;
                    }
                    panels.Add(new TaskPanelRequest { PanelId = $"{e}_{p}", Edge = e, Filter = visible.Contains, PreviousDisplayKeys = previous });
                }
            }

            var input = new TaskModelInput
            {
                Windows = windows,
                Pins = pins,
                TaskOrder = taskOrder,
                Panels = panels,
            };
            Compare(input, $"Multi-panel same-edge randomized trial {trial}");
        }
        Console.WriteLine("PASS: TaskModel.Compute matches the reference implementation across 150 randomized multi-panel-per-edge scenarios (2-3 panels/edge), covering order and pin-reconciliation chaining between panels sharing an edge.");
    }
}
