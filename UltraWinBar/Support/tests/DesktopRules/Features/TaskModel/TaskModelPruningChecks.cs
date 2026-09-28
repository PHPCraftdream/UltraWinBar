using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// Task #127: covers TaskModel.Compute's liveness/pruning pass — LiveKeys is the union of every
// discovered window's key plus any stored key (TaskOrder/TaskbarAssignments/pin PrimaryWindowKey)
// whose window the caller's WindowStillExists delegate says still exists (e.g. on another desktop,
// not yet discovered); dead window:v2: keys are pruned from TaskOrder/TaskbarAssignments exactly
// once per Compute call, not once per edge.
internal static class TaskModelPruningChecks
{
    private const AppBarEdge Edge = AppBarEdge.Bottom;

    private static TaskWindowSnapshot Window(object id, string key) =>
        new() { Window = id, Key = key, PassesFilter = _ => true };

    private static TaskModelInput MakeInput(IReadOnlyList<TaskWindowSnapshot> windows, IReadOnlyList<TaskOrderEntry> taskOrder,
        IReadOnlyList<TaskbarAssignment> assignments = null, IReadOnlyList<PinSnapshot> pins = null,
        Func<string, bool> windowStillExists = null) => new()
    {
        Windows = windows,
        TaskOrder = taskOrder,
        TaskbarAssignments = assignments ?? Array.Empty<TaskbarAssignment>(),
        Pins = pins ?? Array.Empty<PinSnapshot>(),
        EnabledEdges = new[] { Edge },
        WindowStillExists = windowStillExists
    };

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        // A live discovered window's TaskOrder entry survives; a dead window:v2: key with no
        // still-existing window is pruned; a window:v2: key not (yet) discovered but reported
        // alive by WindowStillExists survives too; a non-window (pin:) entry is never touched.
        var live1 = Window("live1", "window:v2:live1");
        var stillAlive2 = "window:v2:stillalive2"; // not discovered, only known via WindowStillExists
        var dead1 = "window:v2:dead1";
        var taskOrder = new List<TaskOrderEntry>
        {
            new() { Edge = Edge, Identifier = "window:v2:live1" },
            new() { Edge = Edge, Identifier = dead1 },
            new() { Edge = Edge, Identifier = stillAlive2 },
            new() { Edge = Edge, Identifier = "pin:exe:app.exe" },
        };
        bool StillExists(string key) => key == stillAlive2;
        var result = TaskModel.Compute(MakeInput(new[] { live1 }, taskOrder, windowStillExists: StillExists));

        if (!result.LiveKeys.SetEquals(new[] { "window:v2:live1", stillAlive2 }))
            throw new Exception("LiveKeys must include every discovered window plus stored window keys reported alive, and nothing else.");
        if (result.PrunedTaskOrder == null)
            throw new Exception("A dead window:v2: TaskOrder entry must be pruned.");
        if (!result.PrunedTaskOrder.Select(e => e.Identifier).SequenceEqual(
            new[] { "window:v2:live1", stillAlive2, "pin:exe:app.exe" }))
            throw new Exception("Pruning must drop only the dead window:v2: entry, keeping live-window and non-window entries in order.");
        Console.WriteLine("PASS: TaskModel.Compute prunes a dead window:v2: TaskOrder entry while keeping live/still-existing window keys and non-window entries.");

        // TaskbarAssignments: only WindowClassAndTitle-mode entries are liveness-pruned; an
        // ExecutablePath-mode entry is never pruned even if its identifier looks like a window key.
        var assignments = new List<TaskbarAssignment>
        {
            new() { Mode = TaskAssignmentMode.WindowClassAndTitle, Identifier = "window:v2:live1", Edge = Edge },
            new() { Mode = TaskAssignmentMode.WindowClassAndTitle, Identifier = dead1, Edge = Edge },
            new() { Mode = TaskAssignmentMode.ExecutablePath, Identifier = dead1, Edge = Edge },
        };
        var assignmentResult = TaskModel.Compute(MakeInput(new[] { live1 }, new List<TaskOrderEntry>(), assignments));
        if (assignmentResult.PrunedTaskbarAssignments == null)
            throw new Exception("A dead window-mode TaskbarAssignment must be pruned.");
        if (!assignmentResult.PrunedTaskbarAssignments.Select(a => (a.Mode, a.Identifier)).SequenceEqual(new[]
            { (TaskAssignmentMode.WindowClassAndTitle, "window:v2:live1"), (TaskAssignmentMode.ExecutablePath, dead1) }))
            throw new Exception("Pruning must only drop the dead WindowClassAndTitle assignment, keeping the ExecutablePath one regardless of its identifier shape.");
        Console.WriteLine("PASS: TaskModel.Compute prunes only dead window-identity TaskbarAssignments, never ExecutablePath ones.");

        // Nothing dead: both prune results are null (no-op), matching PruneDeadWindow*'s contract.
        var cleanOrder = new List<TaskOrderEntry> { new() { Edge = Edge, Identifier = "window:v2:live1" }, new() { Edge = Edge, Identifier = "pin:app" } };
        var clean = TaskModel.Compute(MakeInput(new[] { live1 }, cleanOrder));
        if (clean.PrunedTaskOrder != null || clean.PrunedTaskbarAssignments != null)
            throw new Exception("Compute must not report a prune when nothing is dead.");
        Console.WriteLine("PASS: TaskModel.Compute reports no pruning (null) when every TaskOrder/TaskbarAssignments entry is still live or not a window key.");

        // Pruning happens once per Compute call, not once per edge: with several edges, dead
        // entries for edges other than the current one are still pruned in the single pass.
        var multiEdgeOrder = new List<TaskOrderEntry>
        {
            new() { Edge = AppBarEdge.Bottom, Identifier = "window:v2:live1" },
            new() { Edge = AppBarEdge.Top, Identifier = dead1 },
            new() { Edge = AppBarEdge.Left, Identifier = "window:v2:live1" },
        };
        var multiEdgeInput = new TaskModelInput
        {
            Windows = new[] { live1 },
            TaskOrder = multiEdgeOrder,
            EnabledEdges = new[] { AppBarEdge.Bottom, AppBarEdge.Top, AppBarEdge.Left },
        };
        var multiEdgeResult = TaskModel.Compute(multiEdgeInput);
        if (multiEdgeResult.PrunedTaskOrder == null || multiEdgeResult.PrunedTaskOrder.Any(e => e.Identifier == dead1))
            throw new Exception("A dead entry scoped to one edge must be pruned even though Compute only builds items for that edge once, globally.");
        Console.WriteLine("PASS: TaskModel.Compute prunes dead entries across every edge in one pass, not once per edge.");

        // A pin's PrimaryWindowKey is part of the liveness union too: a dead one is excluded from
        // LiveKeys, and one reported alive by WindowStillExists (but not yet discovered) is kept.
        var pinAlive = new PinSnapshot { Pin = "pinAlive", Edge = Edge, Identifier = "appX", PrimaryWindowKey = stillAlive2, OrderKey = "pin:appX", OnCurrentDesktop = true };
        var pinDead = new PinSnapshot { Pin = "pinDead", Edge = Edge, Identifier = "appY", PrimaryWindowKey = dead1, OrderKey = "pin:appY", OnCurrentDesktop = true };
        var pinLiveKeysResult = TaskModel.Compute(MakeInput(Array.Empty<TaskWindowSnapshot>(), new List<TaskOrderEntry>(),
            pins: new[] { pinAlive, pinDead }, windowStillExists: StillExists));
        if (!pinLiveKeysResult.LiveKeys.Contains(stillAlive2) || pinLiveKeysResult.LiveKeys.Contains(dead1))
            throw new Exception("A pin's PrimaryWindowKey must feed the same WindowStillExists-checked liveness union as TaskOrder/TaskbarAssignments.");
        Console.WriteLine("PASS: a pin's PrimaryWindowKey participates in the LiveKeys union exactly like TaskOrder/TaskbarAssignments identifiers.");
    }
}
