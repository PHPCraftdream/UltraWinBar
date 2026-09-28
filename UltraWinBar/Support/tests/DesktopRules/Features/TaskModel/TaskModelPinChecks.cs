using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// Task #127: covers TaskModel.Compute's pin handling — every branch of a pin's representative-
// window pick (PrimaryWindowKey match, previous display key match, an untracked window, plain
// first-available), pin deduplication by Identifier, PrimaryWindowKey reconciliation, and windows
// split across edges by their per-edge filter (a multi-panel snapshot in one Compute call).
internal static class TaskModelPinChecks
{
    private const AppBarEdge Edge = AppBarEdge.Bottom;

    private static TaskWindowSnapshot Window(object id, string key, string appId, Func<AppBarEdge, bool> filter = null) =>
        new() { Window = id, Key = key, FallbackKey = "hwnd:" + id, AppIdentifier = appId, PassesFilter = filter ?? (_ => true) };

    private static PinSnapshot Pin(object id, string identifier, string primaryWindowKey, AppBarEdge edge = Edge, bool onCurrentDesktop = true) =>
        new() { Pin = id, Edge = edge, Identifier = identifier, PrimaryWindowKey = primaryWindowKey, OrderKey = "pin:" + identifier, OnCurrentDesktop = onCurrentDesktop };

    private static TaskModelInput MakeInput(IReadOnlyList<TaskWindowSnapshot> windows, IReadOnlyList<PinSnapshot> pins,
        IReadOnlyList<TaskOrderEntry> taskOrder = null, IReadOnlyList<AppBarEdge> edges = null,
        IReadOnlyDictionary<AppBarEdge, IReadOnlyDictionary<object, string>> previousDisplayKeys = null) => new()
    {
        Windows = windows,
        Pins = pins,
        TaskOrder = taskOrder ?? Array.Empty<TaskOrderEntry>(),
        EnabledEdges = edges ?? new[] { Edge },
        PreviousDisplayKeys = previousDisplayKeys
    };

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunRepresentativeSelectionChecks();
        RunDeduplicationAndClaimChecks();
        RunReconciliationChecks();
        RunMultiEdgeChecks();
    }

    private static void RunRepresentativeSelectionChecks()
    {
        // Branch 1: PrimaryWindowKey match wins regardless of position in the group.
        var a1 = Window("a1", "kA1", "app");
        var a2 = Window("a2", "kA2", "app");
        var pinA = Pin("pinA", "app", primaryWindowKey: "kA2");
        var byPrimary = TaskModel.Compute(MakeInput(new[] { a1, a2 }, new[] { pinA }));
        if (!byPrimary.Edges[Edge].Items.SequenceEqual(new object[] { "a2", "a1" }))
            throw new Exception("The window matching PrimaryWindowKey must be chosen as the pin's representative.");
        var pinAReversed = Pin("pinA", "app", primaryWindowKey: "kA1");
        var byPrimaryReversed = TaskModel.Compute(MakeInput(new[] { a1, a2 }, new[] { pinAReversed }));
        if (!byPrimaryReversed.Edges[Edge].Items.SequenceEqual(new object[] { "a1", "a2" }))
            throw new Exception("PrimaryWindowKey selection must track which window it names, not always the first or last.");
        Console.WriteLine("PASS: a pin's representative window is the one matching PrimaryWindowKey, whichever position it is in.");

        // Branch 2: no PrimaryWindowKey match, but the previous rebuild's DisplayKeys shows this
        // window was displayed under the pin's OrderKey (i.e. it was the chosen representative
        // last time), so it is kept.
        var b1 = Window("b1", "kB1", "app");
        var b2 = Window("b2", "kB2", "app");
        var pinB = Pin("pinB", "app", primaryWindowKey: "kDead");
        var previousForB = new Dictionary<object, string> { ["b2"] = "pin:app" };
        var previousB = new Dictionary<AppBarEdge, IReadOnlyDictionary<object, string>> { [Edge] = previousForB };
        var byPrevious = TaskModel.Compute(MakeInput(new[] { b1, b2 }, new[] { pinB }, previousDisplayKeys: previousB));
        if (!byPrevious.Edges[Edge].Items.SequenceEqual(new object[] { "b2", "b1" }))
            throw new Exception("With no PrimaryWindowKey match, the window previously displayed under the pin's OrderKey must be kept as representative.");
        Console.WriteLine("PASS: a pin falls back to its previous representative window (by last rebuild's DisplayKeys) when PrimaryWindowKey no longer matches.");

        // Branch 3: no PrimaryWindowKey or previous-key match — prefer a window not already
        // tracked in the saved order (a "new" window), regardless of group position. Checked via
        // DisplayKeys (which item got the pin's OrderKey) rather than final display order: once
        // chosen, the representative sorts by the pin's own OrderKey position (here: unranked,
        // since only "kC1" — the window's own key — has a saved-order slot), not by whichever
        // window it used to be, so the *display* order alone would not isolate the selection.
        var trackedOrder = new List<TaskOrderEntry> { new() { Edge = Edge, Identifier = "kC1" } };
        var c1 = Window("c1", "kC1", "app"); // already tracked
        var c2 = Window("c2", "kC2", "app"); // untracked/new
        var pinC = Pin("pinC", "app", primaryWindowKey: "kDead");
        foreach (var group in new[] { new[] { c1, c2 }, new[] { c2, c1 } })
        {
            var byUntracked = TaskModel.Compute(MakeInput(group, new[] { pinC }, trackedOrder));
            var displayKeys = byUntracked.Edges[Edge].DisplayKeys;
            if (displayKeys["c2"] != "pin:app" || displayKeys["c1"] != "kC1")
                throw new Exception("With no key/previous match, an untracked (not-yet-in-saved-order) window must be preferred as representative.");
        }
        Console.WriteLine("PASS: a pin prefers an untracked window as its representative over one already in the saved order, independent of group order.");

        // Branch 4: every candidate already tracked and no other match — first in group order wins.
        var bothTrackedOrder = new List<TaskOrderEntry>
        {
            new() { Edge = Edge, Identifier = "kD1" },
            new() { Edge = Edge, Identifier = "kD2" },
        };
        var d1 = Window("d1", "kD1", "app");
        var d2 = Window("d2", "kD2", "app");
        var pinD = Pin("pinD", "app", primaryWindowKey: "kDead");
        var firstD1 = TaskModel.Compute(MakeInput(new[] { d1, d2 }, new[] { pinD }, bothTrackedOrder));
        var keysD1 = firstD1.Edges[Edge].DisplayKeys;
        if (keysD1["d1"] != "pin:app" || keysD1["d2"] != "kD2")
            throw new Exception("With no other match and every candidate tracked, the first window in group order must be chosen.");
        var firstD2 = TaskModel.Compute(MakeInput(new[] { d2, d1 }, new[] { pinD }, bothTrackedOrder));
        var keysD2 = firstD2.Edges[Edge].DisplayKeys;
        if (keysD2["d2"] != "pin:app" || keysD2["d1"] != "kD1")
            throw new Exception("The final fallback must be group order, not window identity.");
        Console.WriteLine("PASS: with no key/previous/untracked match, a pin's representative falls back to the first window in group order.");
    }

    private static void RunDeduplicationAndClaimChecks()
    {
        // Two pins for the same app: the first (by pins-list order) wins, the second is dropped —
        // and its windows are still claimed once by the surviving pin, not split between both.
        var e1 = Window("e1", "kE1", "app");
        var e2 = Window("e2", "kE2", "app");
        var firstPin = Pin("first", "app", primaryWindowKey: "kE1");
        var secondPin = Pin("second", "app", primaryWindowKey: "kE2");
        var deduped = TaskModel.Compute(MakeInput(new[] { e1, e2 }, new[] { firstPin, secondPin }));
        var items = deduped.Edges[Edge].Items;
        if (items.Contains("second"))
            throw new Exception("A duplicate pin (same Identifier) must be dropped; only the first-listed pin survives.");
        if (!deduped.Edges[Edge].DisplayKeys.TryGetValue("e1", out string e1Key) || e1Key != "pin:app")
            throw new Exception("The surviving pin's Identifier must claim the app's windows.");
        if (!items.SequenceEqual(new object[] { "e1", "e2" }))
            throw new Exception("A dropped duplicate pin must not change which windows are claimed or their order.");
        Console.WriteLine("PASS: pins are deduplicated by Identifier (first one wins) and still claim the whole app's window group.");

        // Two different apps, two pins: each claims only its own group, independent of the other.
        var f1 = Window("f1", "kF1", "appF");
        var g1 = Window("g1", "kG1", "appG");
        var pinF = Pin("pinF", "appF", primaryWindowKey: "kF1");
        var pinG = Pin("pinG", "appG", primaryWindowKey: "kG1");
        var independent = TaskModel.Compute(MakeInput(new[] { f1, g1 }, new[] { pinF, pinG }));
        if (!independent.Edges[Edge].Items.ToHashSet().SetEquals(new object[] { "f1", "g1" }))
            throw new Exception("Independent pins must each claim only their own app's windows.");
        Console.WriteLine("PASS: pins for different applications claim only their own window group.");
    }

    private static void RunReconciliationChecks()
    {
        // Dead PrimaryWindowKey + a newly selected window: the pin adopts the new key.
        var h1 = Window("h1", "kH1", "app");
        var pinDead = Pin("pinDead", "app", primaryWindowKey: "kDeadH");
        var deadResult = TaskModel.Compute(MakeInput(new[] { h1 }, new[] { pinDead }));
        var deadChange = deadResult.PinPrimaryWindowKeyChanges.SingleOrDefault(c => Equals(c.Pin, "pinDead"));
        if (deadChange.Pin == null || deadChange.NewPrimaryWindowKey != "kH1")
            throw new Exception("A pin whose PrimaryWindowKey names no live window must adopt the newly selected window's key.");

        // Null PrimaryWindowKey: adopts the selected window's key too.
        var i1 = Window("i1", "kI1", "app");
        var pinNull = Pin("pinNull", "app", primaryWindowKey: null);
        var nullResult = TaskModel.Compute(MakeInput(new[] { i1 }, new[] { pinNull }));
        var nullChange = nullResult.PinPrimaryWindowKeyChanges.SingleOrDefault(c => Equals(c.Pin, "pinNull"));
        if (nullChange.Pin == null || nullChange.NewPrimaryWindowKey != "kI1")
            throw new Exception("A pin with no recorded PrimaryWindowKey must adopt the newly selected window's key.");

        // Already-matching PrimaryWindowKey: no change reported.
        var j1 = Window("j1", "kJ1", "app");
        var pinMatch = Pin("pinMatch", "app", primaryWindowKey: "kJ1");
        var matchResult = TaskModel.Compute(MakeInput(new[] { j1 }, new[] { pinMatch }));
        if (matchResult.PinPrimaryWindowKeyChanges.Any(c => Equals(c.Pin, "pinMatch")))
            throw new Exception("A pin whose PrimaryWindowKey already matches the selected window must not report a change.");

        // Two live windows of the same app: PrimaryWindowKey stays put, never flips to the other.
        var k1 = Window("k1", "kK1", "app");
        var k2 = Window("k2", "kK2", "app");
        var pinLive = Pin("pinLive", "app", primaryWindowKey: "kK1");
        var liveResult = TaskModel.Compute(MakeInput(new[] { k1, k2 }, new[] { pinLive }));
        if (liveResult.PinPrimaryWindowKeyChanges.Any(c => Equals(c.Pin, "pinLive")))
            throw new Exception("A pin's PrimaryWindowKey must not flip between two live windows of the same application.");

        // No window at all for the pin's app: the pin itself is the item, and no reconciliation happens.
        var pinOrphan = Pin("pinOrphan", "missingApp", primaryWindowKey: "kWhatever");
        var orphanResult = TaskModel.Compute(MakeInput(Array.Empty<TaskWindowSnapshot>(), new[] { pinOrphan }));
        if (!orphanResult.Edges[Edge].Items.SequenceEqual(new object[] { "pinOrphan" }))
            throw new Exception("A pin with no matching window must appear as itself in the item list.");
        if (orphanResult.PinPrimaryWindowKeyChanges.Any(c => Equals(c.Pin, "pinOrphan")))
            throw new Exception("A pin with no matching window must not attempt PrimaryWindowKey reconciliation.");

        Console.WriteLine("PASS: pin PrimaryWindowKey reconciliation adopts a newly selected window when dead/null, stays put when already correct or when both candidate windows are live, and is skipped entirely when no window matches.");
    }

    private static void RunMultiEdgeChecks()
    {
        const AppBarEdge top = AppBarEdge.Top;
        var bottomOnly = Window("bottomOnly", "kBottom", null, filter: e => e == Edge);
        var topOnly = Window("topOnly", "kTop", null, filter: e => e == top);
        var both = Window("both", "kBoth", null, filter: _ => true);
        var pinBottom = Pin("pinBottom", "appBottom", primaryWindowKey: null, edge: Edge);

        var result = TaskModel.Compute(MakeInput(new[] { bottomOnly, topOnly, both }, new[] { pinBottom }, edges: new[] { Edge, top }));

        if (!result.Edges[Edge].Items.ToHashSet().SetEquals(new object[] { "bottomOnly", "both", "pinBottom" }))
            throw new Exception("A window whose filter fails an edge must not appear in that edge's items.");
        if (!result.Edges[top].Items.ToHashSet().SetEquals(new object[] { "topOnly", "both" }))
            throw new Exception("Each edge must independently include only the windows that pass its own filter.");
        if (result.Edges[top].Items.Contains("pinBottom") || !result.Edges[Edge].DisplayKeys.ContainsKey("bottomOnly"))
            throw new Exception("A pin scoped to one edge must not appear on another edge's task list.");
        Console.WriteLine("PASS: TaskModel.Compute builds every edge's window/pin list independently from one snapshot, split by each window's per-edge filter and each pin's own Edge.");
    }
}
