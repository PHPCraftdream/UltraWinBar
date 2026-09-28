using ManagedShell.AppBar;
using System;
using System.Collections.Generic;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// One window from the task source (Tasks.GroupedWindows.SourceCollection), in source order,
    /// with every value TaskModel.Compute needs already resolved by the caller. Keeping these as
    /// plain data (instead of an ApplicationWindow + Tasks pair) is what lets TaskModel stay free
    /// of ManagedShell, native calls and WPF. Window carries the original window's identity so the
    /// caller can bind the model's output items straight to the UI, and so a TaskPanelRequest's
    /// Filter (which runs over Window, e.g. a panel's own Tasks_Filter) can be reused unchanged.
    /// </summary>
    public sealed class TaskWindowSnapshot
    {
        /// <summary>Identity of the source window (e.g. the ApplicationWindow instance). Never null.</summary>
        public object Window { get; init; }

        /// <summary>TaskOrderIdentifier.Get(window, tasks). Null only if Window itself were null.</summary>
        public string Key { get; init; }

        /// <summary>"hwnd:" + handle — used only when Key is null (defensive fallback, mirrors the
        /// original TaskList.Pins.cs code; Key is never actually null for a real window).</summary>
        public string FallbackKey { get; init; }

        /// <summary>TaskOrderIdentifier.GetLegacyBatch's result for this window. Computed once for
        /// the whole window set: the ordinal it depends on comes from a scan of every ShowInTaskbar
        /// sibling regardless of edge, so it is the same value no matter which panel asks for it.</summary>
        public string LegacyKey { get; init; }

        /// <summary>TaskAssignmentManager.GetIdentifier(window, ExecutablePath). Null if unidentifiable
        /// (never grouped under a pin).</summary>
        public string AppIdentifier { get; init; }
    }

    /// <summary>
    /// One PinnedApplication entry, across every edge/desktop (liveness needs every pin's
    /// PrimaryWindowKey, not just the ones on the edge/desktop being built).
    /// </summary>
    public sealed class PinSnapshot
    {
        /// <summary>Identity of the source pin (the PinnedApplication instance).</summary>
        public object Pin { get; init; }
        public AppBarEdge Edge { get; init; }
        public string Identifier { get; init; }
        public string PrimaryWindowKey { get; init; }
        public string OrderKey { get; init; }
        public bool OnCurrentDesktop { get; init; }
    }

    /// <summary>
    /// One panel's (TaskList's) request into a single Compute call. Several panels can share the
    /// same Edge (Settings.ShowMultiMon: one taskbar per monitor), each with its own Filter (e.g.
    /// scoped to that panel's Host.Screen/HMonitor) and its own PreviousDisplayKeys. Panels on the
    /// same edge are processed in list order and chained: the second panel's starting order is the
    /// first panel's SavedOrder, and a pin reconciled by an earlier panel is seen already-reconciled
    /// by a later one on the same edge — equivalent to the old sequential per-panel rebuild, which
    /// mutated the shared PinnedApplication/TaskOrder state directly between panels.
    /// </summary>
    public sealed class TaskPanelRequest
    {
        /// <summary>Identity of the requesting panel (e.g. the TaskList instance). Keys the result
        /// in TaskModelResult.Panels; must be unique per Compute call.</summary>
        public object PanelId { get; init; }

        public AppBarEdge Edge { get; init; }

        /// <summary>This panel's Tasks_Filter, applied to each TaskWindowSnapshot.Window. Null means
        /// no window passes (mirrors the old per-edge code always supplying a real filter).</summary>
        public Func<object, bool> Filter { get; init; }

        /// <summary>This panel's own DisplayKeys from its previous Compute call (missing/empty =
        /// first-ever rebuild for this panel). Used only for a pin's second-priority
        /// representative-window pick.</summary>
        public IReadOnlyDictionary<object, string> PreviousDisplayKeys { get; init; }
    }

    /// <summary>Everything TaskModel.Compute needs to build every panel's task list in one pass.</summary>
    public sealed class TaskModelInput
    {
        /// <summary>Every window from the task source, in source order (not pre-filtered by panel).</summary>
        public IReadOnlyList<TaskWindowSnapshot> Windows { get; init; } = Array.Empty<TaskWindowSnapshot>();

        /// <summary>Settings.TaskOrder, every edge/desktop scope.</summary>
        public IReadOnlyList<TaskOrderEntry> TaskOrder { get; init; } = Array.Empty<TaskOrderEntry>();

        /// <summary>Settings.TaskbarAssignments.</summary>
        public IReadOnlyList<TaskbarAssignment> TaskbarAssignments { get; init; } = Array.Empty<TaskbarAssignment>();

        /// <summary>Settings.PinnedApplications, every edge/desktop.</summary>
        public IReadOnlyList<PinSnapshot> Pins { get; init; } = Array.Empty<PinSnapshot>();

        /// <summary>Every registered panel's request, in the deterministic order they should be
        /// processed (the caller — the coordinator — owns that order, e.g. registration order).</summary>
        public IReadOnlyList<TaskPanelRequest> Panels { get; init; } = Array.Empty<TaskPanelRequest>();

        public Guid CurrentDesktopId { get; init; }

        /// <summary>TaskOrderIdentifier's WindowStillExists, for stored keys (TaskOrder/TaskbarAssignments/
        /// pin PrimaryWindowKey) not covered by Windows — e.g. a window on another desktop right after
        /// startup. Native by nature, so it is a delegate here; null is treated as "no other window is
        /// still alive" (real callers always pass the native check).</summary>
        public Func<string, bool> WindowStillExists { get; init; }
    }

    /// <summary>One panel's slice of the model output — everything a TaskList panel needs to display.</summary>
    public sealed class TaskModelEdgeResult
    {
        /// <summary>Windows and pins (as their original Window/Pin references), in final display order.</summary>
        public IReadOnlyList<object> Items { get; init; }

        /// <summary>Item -> order key, to feed the next Compute call's PreviousDisplayKeys and to drive
        /// ReorderTask/TogglePin the way TaskList.Pins.cs's displayKeys field used to.</summary>
        public IReadOnlyDictionary<object, string> DisplayKeys { get; init; }

        /// <summary>The order after this panel's processing — for the last panel on this edge, this
        /// equals TaskModelResult.SavedOrders[edge], the value to persist via SetTaskOrderForEdge.</summary>
        public List<string> SavedOrder { get; init; }
    }

    /// <summary>A pin whose PrimaryWindowKey should change to NewPrimaryWindowKey (net of every panel
    /// that touched it on its edge). Pin is the same reference as the originating PinSnapshot.Pin, so
    /// the caller can find and mutate the real object.</summary>
    public readonly struct PinPrimaryWindowKeyChange
    {
        public object Pin { get; init; }
        public string NewPrimaryWindowKey { get; init; }
    }

    /// <summary>Result of one TaskModel.Compute call: everything every panel and Settings need.</summary>
    public sealed class TaskModelResult
    {
        /// <summary>Every window:v2 key with a live window, across every edge/desktop.</summary>
        public IReadOnlySet<string> LiveKeys { get; init; }

        /// <summary>Pruned TaskOrder, or null if nothing was dead (mirrors TaskOrderIdentifier.PruneDeadWindowEntries).</summary>
        public List<TaskOrderEntry> PrunedTaskOrder { get; init; }

        /// <summary>Pruned TaskbarAssignments, or null if nothing was dead.</summary>
        public List<TaskbarAssignment> PrunedTaskbarAssignments { get; init; }

        /// <summary>Net PrimaryWindowKey change per touched pin (original input value -> final value
        /// after every panel on its edge ran), one entry per changed pin.</summary>
        public IReadOnlyList<PinPrimaryWindowKeyChange> PinPrimaryWindowKeyChanges { get; init; }

        /// <summary>Each panel's result, keyed by TaskPanelRequest.PanelId.</summary>
        public IReadOnlyDictionary<object, TaskModelEdgeResult> Panels { get; init; }

        /// <summary>The final order to persist per edge (via SetTaskOrderForEdge), i.e. the SavedOrder
        /// of the last panel processed on that edge — already includes every panel's newly-discovered
        /// keys on that edge, same as sequential per-panel SetTaskOrderForEdge calls would leave behind.</summary>
        public IReadOnlyDictionary<AppBarEdge, List<string>> SavedOrders { get; init; }
    }
}
