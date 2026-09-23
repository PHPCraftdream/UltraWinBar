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
            var windows = taskbarItems?.Cast<object>().OfType<ApplicationWindow>().ToList()
                ?? new List<ApplicationWindow>();
            var pins = Settings.Instance.PinnedApplications.Where(p => p.Edge == HostEdge && p.OnCurrentDesktop)
                .GroupBy(p => p.Identifier).Select(g => g.First()).ToList();
            var order = Settings.Instance.GetTaskOrderForEdge(HostEdge);
            foreach (var window in windows)
            {
                string key = TaskOrderIdentifier.Get(window, Tasks);
                if (order.Contains(key)) continue;
                int legacyIndex = order.IndexOf(TaskOrderIdentifier.GetLegacy(window, Tasks));
                if (legacyIndex >= 0) order[legacyIndex] = key;
            }
            bool pinsChanged = false;
            var keys = new Dictionary<object, string>();
            var claimed = new HashSet<ApplicationWindow>();
            var items = new List<object>();
            foreach (var pin in pins)
            {
                var group = windows.Where(w => !claimed.Contains(w) &&
                    TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath) == pin.Identifier).ToList();
                var window = group.FirstOrDefault(w => TaskOrderIdentifier.Get(w, Tasks) == pin.PrimaryWindowKey)
                    ?? group.FirstOrDefault(w => displayKeys.TryGetValue(w, out string key) && key == pin.OrderKey)
                    ?? group.FirstOrDefault(w => !order.Contains(TaskOrderIdentifier.Get(w, Tasks)))
                    ?? group.FirstOrDefault();
                if (window != null && !TaskOrderIdentifier.IsAlive(pin.PrimaryWindowKey))
                {
                    pin.PrimaryWindowKey = TaskOrderIdentifier.Get(window, Tasks);
                    pinsChanged = true;
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
            items = items.OrderBy(item =>
            {
                int index = order.IndexOf(keys[item]);
                return index < 0 ? int.MaxValue : index;
            }).ToList();
            displayKeys = keys;
            for (int i = displayedTasks.Count - 1; i >= 0; i--)
                if (!items.Contains(displayedTasks[i])) displayedTasks.RemoveAt(i);
            for (int i = 0; i < items.Count; i++)
            {
                int oldIndex = displayedTasks.IndexOf(items[i]);
                if (oldIndex < 0) displayedTasks.Insert(i, items[i]);
                else if (oldIndex != i) displayedTasks.Move(oldIndex, i);
            }
            foreach (var item in items)
                if (!order.Contains(keys[item])) order.Add(keys[item]);
            Settings.Instance.SetTaskOrderForEdge(HostEdge, order);
            if (pinsChanged) Settings.Instance.PinnedApplications = Settings.Instance.PinnedApplications.ToList();
            string snapshot = string.Join("|", displayedTasks.Select(item => displayKeys[item]));
            if (snapshot != lastOrderSnapshot)
            {
                lastOrderSnapshot = snapshot;
                ShellLogger.Debug($"Task order snapshot: desktop={VirtualDesktopContext.Instance?.CurrentId}; edge={HostEdge}; keys={snapshot}");
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
            order.AddRange(Settings.Instance.GetTaskOrderForEdge(HostEdge).Where(id => !order.Contains(id)));
            Settings.Instance.SetTaskOrderForEdge(HostEdge, order);
            SetTaskButtonWidth();
            ShellLogger.Debug($"Task reorder: edge={HostEdge}; from={oldIndex}; to={index}; key={key}");
        }
    }
}
