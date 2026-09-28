using ManagedShell.AppBar;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// Pure replacement for TaskList.Pins.cs's RebuildDisplayedTasks: from one snapshot, computes
    /// what every registered panel needs (order, pins-vs-windows grouping, TaskOrder/TaskbarAssignments
    /// pruning, pin PrimaryWindowKey reconciliation) in a single pass instead of once per panel.
    /// Several panels may share one Edge (multi-monitor: one taskbar per screen) — see
    /// TaskPanelRequest for how same-edge panels are chained. No Settings.Instance, WPF,
    /// VirtualDesktopContext or native calls — everything comes from TaskModelInput, so this is
    /// testable without a Dispatcher or a real window. See TaskModelHost (#128) for the wiring.
    /// </summary>
    public static class TaskModel
    {
        private static readonly List<TaskWindowSnapshot> EmptyWindows = new();
        private static readonly IReadOnlyDictionary<object, string> EmptyDisplayKeys = new Dictionary<object, string>();

        public static TaskModelResult Compute(TaskModelInput input)
        {
            IReadOnlyList<TaskWindowSnapshot> windows = input.Windows ?? Array.Empty<TaskWindowSnapshot>();
            IReadOnlyList<PinSnapshot> pins = input.Pins ?? Array.Empty<PinSnapshot>();
            IReadOnlyList<TaskOrderEntry> taskOrderIn = input.TaskOrder ?? Array.Empty<TaskOrderEntry>();
            IReadOnlyList<TaskbarAssignment> assignmentsIn = input.TaskbarAssignments ?? Array.Empty<TaskbarAssignment>();
            Func<string, bool> windowStillExists = input.WindowStillExists ?? (_ => false);

            // Mirrors TaskOrderIdentifier.LiveKeys: every discovered window's key, plus any stored
            // key (TaskOrder/TaskbarAssignments/pin PrimaryWindowKey) whose window still exists but
            // hasn't been (re)discovered yet — e.g. on another desktop right after startup.
            var liveKeys = new HashSet<string>();
            foreach (var window in windows)
                if (window.Key != null) liveKeys.Add(window.Key);
            foreach (var key in StoredKeys(taskOrderIn, assignmentsIn, pins))
                if (key != null && !liveKeys.Contains(key) && windowStillExists(key)) liveKeys.Add(key);

            // Pruning is global (every edge/desktop scope at once) and done once, unlike the
            // original per-panel RebuildDisplayedTasks, which recomputed the identical prune on
            // every one of up to four panels.
            List<TaskOrderEntry> prunedTaskOrder = TaskOrderIdentifier.PruneDeadWindowEntries(new List<TaskOrderEntry>(taskOrderIn), liveKeys);
            List<TaskbarAssignment> prunedAssignments = TaskOrderIdentifier.PruneDeadWindowAssignments(new List<TaskbarAssignment>(assignmentsIn), liveKeys);
            List<TaskOrderEntry> effectiveTaskOrder = prunedTaskOrder ?? new List<TaskOrderEntry>(taskOrderIn);

            var panelResults = new Dictionary<object, TaskModelEdgeResult>();
            var edgeOrders = new Dictionary<AppBarEdge, List<string>>();
            // A pin reconciled by one panel must be seen already-reconciled by the next panel on the
            // same edge (see TaskPanelRequest doc) — mirrors the old code mutating the shared
            // PinnedApplication instance directly between sequential per-panel rebuilds.
            var pinPrimaryOverride = new Dictionary<object, string>();

            foreach (var panel in input.Panels ?? Array.Empty<TaskPanelRequest>())
            {
                if (!edgeOrders.TryGetValue(panel.Edge, out List<string> orderIn))
                    orderIn = GetOrderForEdge(effectiveTaskOrder, panel.Edge, input.CurrentDesktopId);

                TaskModelEdgeResult result = ComputePanel(panel, orderIn, windows, pins, liveKeys, pinPrimaryOverride);
                edgeOrders[panel.Edge] = result.SavedOrder;
                panelResults[panel.PanelId] = result;
            }

            var pinChanges = new List<PinPrimaryWindowKeyChange>();
            foreach (var pin in pins)
            {
                if (pinPrimaryOverride.TryGetValue(pin.Pin, out string finalKey) && finalKey != pin.PrimaryWindowKey)
                    pinChanges.Add(new PinPrimaryWindowKeyChange { Pin = pin.Pin, NewPrimaryWindowKey = finalKey });
            }

            return new TaskModelResult
            {
                LiveKeys = liveKeys,
                PrunedTaskOrder = prunedTaskOrder,
                PrunedTaskbarAssignments = prunedAssignments,
                PinPrimaryWindowKeyChanges = pinChanges,
                Panels = panelResults,
                SavedOrders = edgeOrders
            };
        }

        private static IEnumerable<string> StoredKeys(IReadOnlyList<TaskOrderEntry> taskOrder,
            IReadOnlyList<TaskbarAssignment> assignments, IReadOnlyList<PinSnapshot> pins)
        {
            foreach (var entry in taskOrder) yield return entry.Identifier;
            foreach (var assignment in assignments) yield return assignment.Identifier;
            foreach (var pin in pins) yield return pin.PrimaryWindowKey;
        }

        /// <summary>One panel's body of the original RebuildDisplayedTasks, translated line for line,
        /// generalized from "this edge's one filter" to "this panel's own Filter/PreviousDisplayKeys",
        /// and starting from orderIn (the previous same-edge panel's SavedOrder, or the saved
        /// TaskOrder for the first panel on an edge) instead of always re-reading Settings.</summary>
        private static TaskModelEdgeResult ComputePanel(TaskPanelRequest panel, List<string> orderIn,
            IReadOnlyList<TaskWindowSnapshot> allWindows, IReadOnlyList<PinSnapshot> allPins,
            HashSet<string> liveKeys, Dictionary<object, string> pinPrimaryOverride)
        {
            IReadOnlyDictionary<object, string> previousDisplayKeys = panel.PreviousDisplayKeys ?? EmptyDisplayKeys;

            var windows = new List<TaskWindowSnapshot>();
            foreach (var window in allWindows)
                if (panel.Filter?.Invoke(window.Window) == true) windows.Add(window);

            var pins = new List<PinSnapshot>();
            var seenPinIds = new HashSet<string>();
            foreach (var pin in allPins)
                if (pin.Edge == panel.Edge && pin.OnCurrentDesktop && seenPinIds.Add(pin.Identifier)) pins.Add(pin);

            List<string> order = new List<string>(orderIn);
            var orderSet = new HashSet<string>(order);
            var legacyIndex = new Dictionary<string, int>();
            for (int i = 0; i < order.Count; i++)
                if (order[i] != null && !legacyIndex.ContainsKey(order[i])) legacyIndex[order[i]] = i;

            foreach (var window in windows)
            {
                string key = window.Key;
                if (orderSet.Contains(key)) continue;
                string legacyKey = window.LegacyKey;
                if (legacyKey != null && legacyIndex.TryGetValue(legacyKey, out int index))
                {
                    orderSet.Remove(order[index]);
                    order[index] = key;
                    orderSet.Add(key);
                    legacyIndex.Remove(legacyKey);
                    legacyIndex[key] = index;
                }
            }

            var keys = new Dictionary<object, string>();
            var claimed = new HashSet<TaskWindowSnapshot>();
            var items = new List<object>();
            var windowsByAppId = GroupByAppIdentifier(windows);

            foreach (var pin in pins)
            {
                var group = (windowsByAppId.TryGetValue(pin.Identifier, out var candidates) ? candidates : EmptyWindows)
                    .Where(w => !claimed.Contains(w)).ToList();
                string effectivePrimary = pinPrimaryOverride.TryGetValue(pin.Pin, out string overridden) ? overridden : pin.PrimaryWindowKey;
                var window = group.FirstOrDefault(w => w.Key == effectivePrimary)
                    ?? group.FirstOrDefault(w => previousDisplayKeys.TryGetValue(w.Window, out string key) && key == pin.OrderKey)
                    ?? group.FirstOrDefault(w => !orderSet.Contains(w.Key))
                    ?? group.FirstOrDefault();
                if (window != null)
                {
                    string reconciled = TaskOrderIdentifier.ReconcilePrimaryWindowKey(effectivePrimary, window.Key, liveKeys);
                    if (reconciled != effectivePrimary)
                        pinPrimaryOverride[pin.Pin] = reconciled;
                }
                object item = window != null ? window.Window : pin.Pin;
                foreach (var member in group) claimed.Add(member);
                items.Add(item);
                keys[item] = pin.OrderKey;
                foreach (var member in group.Where(w => !ReferenceEquals(w, window)))
                {
                    items.Add(member.Window);
                    keys[member.Window] = member.Key ?? member.FallbackKey;
                }
            }
            foreach (var window in windows.Where(w => !claimed.Contains(w)))
            {
                items.Add(window.Window);
                keys[window.Window] = window.Key ?? window.FallbackKey;
            }

            var orderIndexes = new Dictionary<string, int>();
            int nullOrderIndex = -1;
            for (int i = 0; i < order.Count; i++)
            {
                string orderKey = order[i];
                if (orderKey == null)
                {
                    if (nullOrderIndex < 0) nullOrderIndex = i;
                }
                else if (!orderIndexes.ContainsKey(orderKey))
                {
                    orderIndexes.Add(orderKey, i);
                }
            }
            items = items.OrderBy(item =>
            {
                string itemKey = keys[item];
                int index = itemKey == null
                    ? nullOrderIndex
                    : orderIndexes.TryGetValue(itemKey, out int orderIndex) ? orderIndex : -1;
                return index < 0 ? int.MaxValue : index;
            }).ToList();

            foreach (var item in items)
                if (orderSet.Add(keys[item])) order.Add(keys[item]);

            return new TaskModelEdgeResult { Items = items, DisplayKeys = keys, SavedOrder = order };
        }

        // Mirrors TaskAssignmentManager.GroupByExecutableIdentifier, but over TaskWindowSnapshot's
        // precomputed AppIdentifier instead of calling into ManagedShell for each window.
        private static Dictionary<string, List<TaskWindowSnapshot>> GroupByAppIdentifier(List<TaskWindowSnapshot> windows)
        {
            var groups = new Dictionary<string, List<TaskWindowSnapshot>>();
            foreach (var window in windows)
            {
                string id = window.AppIdentifier;
                if (id == null) continue;
                if (!groups.TryGetValue(id, out var group)) groups[id] = group = new List<TaskWindowSnapshot>();
                group.Add(window);
            }
            return groups;
        }

        /// <summary>Mirrors Settings.GetTaskOrderForEdge: entries scoped to desktopId if any exist for
        /// this edge, else the edge's Guid.Empty (unscoped/default) entries. Always a fresh list.</summary>
        public static List<string> GetOrderForEdge(IReadOnlyList<TaskOrderEntry> taskOrder, AppBarEdge edge, Guid desktopId)
        {
            bool hasScopedOrder = false;
            foreach (var entry in taskOrder)
            {
                if (entry.Edge == edge && entry.DesktopId == desktopId) { hasScopedOrder = true; break; }
            }

            Guid scope = hasScopedOrder ? desktopId : Guid.Empty;
            var result = new List<string>();
            foreach (var entry in taskOrder)
                if (entry.Edge == edge && entry.DesktopId == scope) result.Add(entry.Identifier);
            return result;
        }

        /// <summary>Mirrors Settings.SetTaskOrderForEdge's entries-replace logic (minus persistence
        /// and the unchanged-write skip, which are Settings' concerns, not this pure function's):
        /// every other edge/desktop scope's entries kept as-is, this edge/desktopId scope replaced
        /// with 'identifiers'.</summary>
        public static List<TaskOrderEntry> ApplyOrderForEdge(IReadOnlyList<TaskOrderEntry> taskOrder, AppBarEdge edge, Guid desktopId, List<string> identifiers)
        {
            var entries = new List<TaskOrderEntry>();
            foreach (var entry in taskOrder)
                if (entry.Edge != edge || entry.DesktopId != desktopId) entries.Add(entry);
            foreach (var identifier in identifiers)
                entries.Add(new TaskOrderEntry { Edge = edge, Identifier = identifier, DesktopId = desktopId });
            return entries;
        }
    }
}
