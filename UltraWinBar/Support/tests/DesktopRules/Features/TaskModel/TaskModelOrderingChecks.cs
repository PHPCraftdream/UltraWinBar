using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// Task #127: TaskModel.Compute is a pure, WPF-free replacement for TaskList.Pins.cs's
// RebuildDisplayedTasks. These checks cover the per-edge ordering rules on their own: saved
// order wins over discovery order, brand-new windows get appended, legacy order keys migrate to
// window:v2 keys in place, GetTaskOrderForEdge's desktop-scoping semantics, the null-key edge
// case, and idempotence (re-running on the model's own output changes nothing further).
internal static class TaskModelOrderingChecks
{
    private static TaskWindowSnapshot Window(object id, string key, string legacyKey = null, string fallback = null, string appId = null, Func<AppBarEdge, bool> filter = null) =>
        new() { Window = id, Key = key, LegacyKey = legacyKey, FallbackKey = fallback, AppIdentifier = appId, PassesFilter = filter ?? (_ => true) };

    private static TaskModelInput MakeInput(IReadOnlyList<TaskWindowSnapshot> windows, IReadOnlyList<TaskOrderEntry> taskOrder,
        AppBarEdge edge = AppBarEdge.Bottom, Guid desktopId = default,
        IReadOnlyDictionary<AppBarEdge, IReadOnlyDictionary<object, string>> previousDisplayKeys = null) => new()
    {
        Windows = windows,
        TaskOrder = taskOrder,
        EnabledEdges = new[] { edge },
        CurrentDesktopId = desktopId,
        PreviousDisplayKeys = previousDisplayKeys
    };

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        const AppBarEdge edge = AppBarEdge.Bottom;

        // Saved order wins over discovery order, in every discovery permutation.
        var w1 = Window("w1", "k1");
        var w2 = Window("w2", "k2");
        var w3 = Window("w3", "k3");
        var savedOrder = new List<TaskOrderEntry>
        {
            new() { Edge = edge, DesktopId = default, Identifier = "k3" },
            new() { Edge = edge, DesktopId = default, Identifier = "k1" },
            new() { Edge = edge, DesktopId = default, Identifier = "k2" },
        };
        foreach (var discovery in new[] { new[] { w1, w2, w3 }, new[] { w3, w2, w1 }, new[] { w2, w1, w3 } })
        {
            var result = TaskModel.Compute(MakeInput(discovery, savedOrder, edge));
            var items = result.Edges[edge].Items;
            if (!items.SequenceEqual(new object[] { "w3", "w1", "w2" }))
                throw new Exception("Discovery order must not affect the saved display order.");
            if (!result.Edges[edge].SavedOrder.SequenceEqual(new[] { "k3", "k1", "k2" }))
                throw new Exception("An already-complete saved order must round-trip unchanged.");
        }
        Console.WriteLine("PASS: TaskModel.Compute orders items by the saved TaskOrder regardless of window discovery order.");

        // Brand-new windows (no TaskOrder entry) are appended after the saved ones, in their
        // original source order, and their keys get appended to SavedOrder in that same order.
        var existing = new List<TaskOrderEntry> { new() { Edge = edge, DesktopId = default, Identifier = "k1" } };
        var withNew = TaskModel.Compute(MakeInput(new[] { w1, w2, w3 }, existing, edge));
        var newItems = withNew.Edges[edge].Items;
        if (!newItems.SequenceEqual(new object[] { "w1", "w2", "w3" }))
            throw new Exception("New windows must be appended after saved-order windows, in source order.");
        if (!withNew.Edges[edge].SavedOrder.SequenceEqual(new[] { "k1", "k2", "k3" }))
            throw new Exception("New windows' keys must be appended to SavedOrder in the order they were displayed.");
        Console.WriteLine("PASS: TaskModel.Compute appends brand-new windows after the saved order, in source order.");

        // Legacy key migration: an order entry recorded under the old class:/exe:-style key
        // adopts the window's new window:v2 key in place, at the same index, without growing
        // SavedOrder — exactly like RebuildDisplayedTasks's in-place order[index] = key rewrite.
        var legacyWindow = Window("legacyWin", "window:v2:new", legacyKey: "class:Notepad|title:Untitled");
        var legacyOrder = new List<TaskOrderEntry>
        {
            new() { Edge = edge, DesktopId = default, Identifier = "class:Notepad|title:Untitled" },
        };
        var migrated = TaskModel.Compute(MakeInput(new[] { legacyWindow }, legacyOrder, edge));
        if (!migrated.Edges[edge].SavedOrder.SequenceEqual(new[] { "window:v2:new" }))
            throw new Exception("Legacy order key must migrate to the window's new key in place, not append a second entry.");
        if (!migrated.Edges[edge].Items.SequenceEqual(new object[] { "legacyWin" }))
            throw new Exception("The migrated window must still appear in the rebuilt list.");

        // Two windows sharing one legacy key: only the first consumes the migration slot
        // (legacyIndex.Remove after use), the second is treated as brand-new and appended.
        var legacyA = Window("legacyA", "window:v2:a", legacyKey: "class:X|title:Y");
        var legacyB = Window("legacyB", "window:v2:b", legacyKey: "class:X|title:Y");
        var sharedLegacyOrder = new List<TaskOrderEntry> { new() { Edge = edge, DesktopId = default, Identifier = "class:X|title:Y" } };
        var sharedMigrated = TaskModel.Compute(MakeInput(new[] { legacyA, legacyB }, sharedLegacyOrder, edge));
        if (!sharedMigrated.Edges[edge].SavedOrder.SequenceEqual(new[] { "window:v2:a", "window:v2:b" }))
            throw new Exception("Only the first window with a shared legacy key may migrate into its slot; the second must append as new.");
        Console.WriteLine("PASS: TaskModel.Compute migrates a legacy order key to the discovered window's key in place, and only the first of several windows sharing one legacy key claims it.");

        // GetTaskOrderForEdge semantics: scoped entries for the current desktop win if any exist
        // for this edge; otherwise the edge's Guid.Empty (default) entries are used.
        var desktopA = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
        var desktopB = Guid.Parse("00000000-0000-0000-0000-0000000000B2");
        var scopedOrder = new List<TaskOrderEntry>
        {
            new() { Edge = edge, DesktopId = default, Identifier = "default1" },
            new() { Edge = edge, DesktopId = default, Identifier = "default2" },
            new() { Edge = edge, DesktopId = desktopA, Identifier = "scopedA1" },
        };
        if (!TaskModel.GetOrderForEdge(scopedOrder, edge, desktopA).SequenceEqual(new[] { "scopedA1" }))
            throw new Exception("A desktop with a scoped order must use only its own scoped entries.");
        if (!TaskModel.GetOrderForEdge(scopedOrder, edge, desktopB).SequenceEqual(new[] { "default1", "default2" }))
            throw new Exception("A desktop with no scoped order must fall back to the edge's Guid.Empty entries.");
        if (!TaskModel.GetOrderForEdge(scopedOrder, AppBarEdge.Top, desktopA).SequenceEqual(Array.Empty<string>()))
            throw new Exception("An edge with no entries at all must return an empty order.");
        var perDesktopWindow = Window("w", "scopedA1");
        var perDesktopResult = TaskModel.Compute(MakeInput(new[] { perDesktopWindow }, scopedOrder, edge, desktopA));
        if (!perDesktopResult.Edges[edge].SavedOrder.SequenceEqual(new[] { "scopedA1" }))
            throw new Exception("Compute must read the desktop-scoped order for the current desktop, not the default one.");
        Console.WriteLine("PASS: TaskModel.GetOrderForEdge/Compute use a desktop's own scoped TaskOrder entries when present, else the edge's Guid.Empty entries.");

        // Null order keys: an item with no key (both Key and FallbackKey null) sorts into the
        // saved order's null slot when one exists, or appends (and records a null SavedOrder
        // entry) when none does — matching RebuildDisplayedTasks's nullOrderIndex handling.
        var keyless = Window("keyless", null, fallback: null);
        var withNullSlot = new List<TaskOrderEntry>
        {
            new() { Edge = edge, DesktopId = default, Identifier = null },
            new() { Edge = edge, DesktopId = default, Identifier = "k1" },
        };
        var nullSlotResult = TaskModel.Compute(MakeInput(new[] { keyless, w1 }, withNullSlot, edge));
        if (!nullSlotResult.Edges[edge].Items.SequenceEqual(new object[] { "keyless", "w1" }))
            throw new Exception("A keyless item must sort into the saved order's null slot.");
        var noNullSlot = new List<TaskOrderEntry> { new() { Edge = edge, DesktopId = default, Identifier = "k1" } };
        var noNullSlotResult = TaskModel.Compute(MakeInput(new[] { w1, keyless }, noNullSlot, edge));
        if (!noNullSlotResult.Edges[edge].Items.SequenceEqual(new object[] { "w1", "keyless" }))
            throw new Exception("A keyless item must append after the saved order when the saved order has no null slot.");
        if (noNullSlotResult.Edges[edge].SavedOrder.Count != 2 || noNullSlotResult.Edges[edge].SavedOrder[1] != null)
            throw new Exception("Appending a keyless item must record a null SavedOrder entry, matching orderSet.Add(null).");
        Console.WriteLine("PASS: TaskModel.Compute sorts a keyless item into a saved null slot when present, else appends it and records a null SavedOrder entry.");

        // Idempotence: writing SavedOrder back as the new TaskOrder and re-running Compute with
        // the same windows/pins/PreviousDisplayKeys must reproduce the same order and no further
        // pruning or pin changes — the model must not oscillate on its own output.
        var idempotentInput = MakeInput(new[] { w3, w1, w2 }, savedOrder, edge);
        var first = TaskModel.Compute(idempotentInput);
        var edgeFirst = first.Edges[edge];
        var rewritten = TaskModel.ApplyOrderForEdge(savedOrder, edge, default, edgeFirst.SavedOrder);
        var previous = new Dictionary<AppBarEdge, IReadOnlyDictionary<object, string>> { [edge] = edgeFirst.DisplayKeys };
        var second = TaskModel.Compute(MakeInput(new[] { w3, w1, w2 }, rewritten, edge, previousDisplayKeys: previous));
        var edgeSecond = second.Edges[edge];
        if (!edgeSecond.SavedOrder.SequenceEqual(edgeFirst.SavedOrder))
            throw new Exception("Re-running Compute on its own SavedOrder must reproduce the same order.");
        if (!edgeSecond.Items.SequenceEqual(edgeFirst.Items))
            throw new Exception("Re-running Compute on its own output must reproduce the same item order.");
        if (second.PrunedTaskOrder != null || second.PrunedTaskbarAssignments != null)
            throw new Exception("Re-running Compute on live windows must not prune anything further.");
        if (second.PinPrimaryWindowKeyChanges.Count != 0)
            throw new Exception("Re-running Compute with no pins must not report pin changes.");
        Console.WriteLine("PASS: TaskModel.Compute is idempotent — feeding its own SavedOrder and DisplayKeys back in reproduces the same order with no further changes.");
    }
}
