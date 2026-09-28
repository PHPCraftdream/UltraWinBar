using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using UltraWinBar.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace UltraWinBar.Controls
{
    public partial class TaskList
    {
        private readonly ObservableCollection<object> displayedTasks = new ObservableCollection<object>();
        private Dictionary<object, string> displayKeys = new Dictionary<object, string>();
        private bool rebuildPending;
        private string lastOrderSnapshot;
        private static readonly List<ApplicationWindow> EmptyWindows = new List<ApplicationWindow>();

        internal void QueueTaskRebuild()
        {
            if (rebuildPending) return;
            rebuildPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                rebuildPending = false;
                if (!isLoaded) return;
                RebuildDisplayedTasks();
            }), DispatcherPriority.Background);
        }

        private void RebuildDisplayedTasks()
        {
            var liveKeys = TaskOrderIdentifier.LiveKeys(Tasks,
                Settings.Instance.TaskOrder.Select(entry => entry.Identifier)
                    .Concat(Settings.Instance.TaskbarAssignments.Select(assignment => assignment.Identifier))
                    .Concat(Settings.Instance.PinnedApplications.Select(pin => pin.PrimaryWindowKey)));
            Settings.Instance.PruneDeadTaskOrderEntries(liveKeys);
            Settings.Instance.PruneDeadTaskbarAssignments(liveKeys);

            var windows = taskbarItems?.Cast<object>().OfType<ApplicationWindow>().ToList()
                ?? new List<ApplicationWindow>();
            var pins = Settings.Instance.PinnedApplications.Where(p => p.Edge == HostEdge && p.OnCurrentDesktop)
                .GroupBy(p => p.Identifier).Select(g => g.First()).ToList();
            var order = Settings.Instance.GetTaskOrderForEdge(HostEdge);
            var orderSet = new HashSet<string>(order);
            var legacyIndex = new Dictionary<string, int>();
            for (int i = 0; i < order.Count; i++)
                if (order[i] != null && !legacyIndex.ContainsKey(order[i])) legacyIndex[order[i]] = i;
            var legacyKeys = TaskOrderIdentifier.GetLegacyBatch(windows, Tasks);
            foreach (var window in windows)
            {
                string key = TaskOrderIdentifier.Get(window, Tasks);
                if (orderSet.Contains(key)) continue;
                string legacyKey = legacyKeys[window];
                if (legacyKey != null && legacyIndex.TryGetValue(legacyKey, out int index))
                {
                    orderSet.Remove(order[index]);
                    order[index] = key;
                    orderSet.Add(key);
                    legacyIndex.Remove(legacyKey);
                    legacyIndex[key] = index;
                }
            }
            bool pinsChanged = false;
            var keys = new Dictionary<object, string>();
            var claimed = new HashSet<ApplicationWindow>();
            var items = new List<object>();
            var windowsByAppId = TaskAssignmentManager.GroupByExecutableIdentifier(windows);
            foreach (var pin in pins)
            {
                // Pins are deduplicated by Identifier above, so two pins never share an
                // app-identifier group; the claimed filter only matters within this pin's own
                // group (a member already placed as the primary window further down).
                var group = (windowsByAppId.TryGetValue(pin.Identifier, out var candidates) ? candidates : EmptyWindows)
                    .Where(w => !claimed.Contains(w)).ToList();
                var window = group.FirstOrDefault(w => TaskOrderIdentifier.Get(w, Tasks) == pin.PrimaryWindowKey)
                    ?? group.FirstOrDefault(w => displayKeys.TryGetValue(w, out string key) && key == pin.OrderKey)
                    ?? group.FirstOrDefault(w => !orderSet.Contains(TaskOrderIdentifier.Get(w, Tasks)))
                    ?? group.FirstOrDefault();
                if (window != null)
                {
                    string reconciled = TaskOrderIdentifier.ReconcilePrimaryWindowKey(
                        pin.PrimaryWindowKey, TaskOrderIdentifier.Get(window, Tasks), liveKeys);
                    if (reconciled != pin.PrimaryWindowKey)
                    {
                        pin.PrimaryWindowKey = reconciled;
                        pinsChanged = true;
                    }
                }
                object item = window ?? (object)pin;
                foreach (var member in group) claimed.Add(member);
                items.Add(item);
                keys[item] = pin.OrderKey;
                foreach (var member in group.Where(w => !ReferenceEquals(w, window)))
                {
                    items.Add(member);
                    keys[member] = TaskOrderIdentifier.Get(member, Tasks) ?? "hwnd:" + member.Handle;
                }
            }
            foreach (var window in windows.Where(w => !claimed.Contains(w)))
            {
                items.Add(window);
                keys[window] = TaskOrderIdentifier.Get(window, Tasks) ?? "hwnd:" + window.Handle;
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
            displayKeys = keys;
            TaskListDiff.Reconcile(displayedTasks, items);
            foreach (var item in items)
                if (orderSet.Add(keys[item])) order.Add(keys[item]);
            Settings.Instance.SetTaskOrderForEdge(HostEdge, order);
            if (pinsChanged) Settings.Instance.PinnedApplications = Settings.Instance.PinnedApplications.ToList();
            if (Settings.Instance.DebugLogging)
            {
                string snapshot = string.Join("|", displayedTasks.Select(item => displayKeys[item]));
                if (snapshot != lastOrderSnapshot)
                {
                    lastOrderSnapshot = snapshot;
                    ShellLogger.Debug($"Task order snapshot: desktop={VirtualDesktopContext.Instance?.CurrentId}; edge={HostEdge}; keys={snapshot}");
                }
            }
            SetTaskButtonWidth();
        }

        internal bool IsPinned(ApplicationWindow window)
        {
            string id = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            return Settings.Instance.PinnedApplications.Any(p => p.Edge == HostEdge && p.Identifier == id && p.OnCurrentDesktop);
        }

        internal List<ApplicationWindow> GetPinnedWindows(ApplicationWindow window)
        {
            if (window == null || !IsPinned(window)) return new List<ApplicationWindow>();
            string id = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            return taskbarItems.Cast<object>().OfType<ApplicationWindow>().Where(w =>
                TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath) == id).ToList();
        }

        internal void TogglePin(object item)
        {
            try
            {
                var pin = item as PinnedApplication;
                var window = item as ApplicationWindow;
                string id = pin?.Identifier ?? TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
                if (id == null) return;
                var pins = Settings.Instance.PinnedApplications.ToList();
                if (pins.Any(p => p.Edge == HostEdge && p.Identifier == id && p.OnCurrentDesktop))
                    pins.RemoveAll(p => p.Edge == HostEdge && p.Identifier == id && p.OnCurrentDesktop);
                else
                {
                    pin = PinnedApplication.FromWindow(window, HostEdge);
                    if (pin == null) return;
                    pins.Add(pin);
                    var order = Settings.Instance.GetTaskOrderForEdge(HostEdge);
                    if (displayKeys.TryGetValue(item, out string oldKey))
                    {
                        int index = order.IndexOf(oldKey);
                        if (index >= 0) order[index] = pin.OrderKey;
                    }
                    Settings.Instance.SetTaskOrderForEdge(HostEdge, order);
                }
                Settings.Instance.PinnedApplications = pins;
            }
            catch (Exception ex) { ShellLogger.Error($"Pin task failed: {ex}"); }
        }

        public void ReorderTask(object item, Point screenPoint)
        {
            if (!displayKeys.TryGetValue(item, out string key) ||
                !TryGetTaskInsertionTarget(screenPoint, out TaskInsertionTarget target)) return;
            int oldIndex = displayedTasks.IndexOf(item);
            int index = target.InsertIndex - (target.InsertIndex > oldIndex ? 1 : 0);
            index = Math.Max(0, Math.Min(index, displayedTasks.Count - 1));
            if (index == oldIndex) return;
            displayedTasks.Move(oldIndex, index);
            var order = displayedTasks.Select(t => displayKeys[t]).ToList();
            var orderSet = new HashSet<string>(order);
            foreach (var id in Settings.Instance.GetTaskOrderForEdge(HostEdge))
                if (orderSet.Add(id)) order.Add(id);
            Settings.Instance.SetTaskOrderForEdge(HostEdge, order);
            SetTaskButtonWidth();
            ShellLogger.Debug($"Task reorder: edge={HostEdge}; from={oldIndex}; to={index}; key={key}");
        }
    }
}
