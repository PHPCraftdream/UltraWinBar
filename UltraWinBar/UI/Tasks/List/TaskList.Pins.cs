using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using UltraWinBar.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace UltraWinBar.Controls
{
    // Task #128: TaskList no longer computes its own LiveKeys/pruning/order — TaskModelHost runs
    // one TaskModel.Compute pass per dispatcher tick covering every registered panel, and hands
    // each panel its own slice back through ITaskModelPanel.Apply.
    public partial class TaskList : ITaskModelPanel
    {
        private string lastOrderSnapshot;

        internal void QueueTaskRebuild() => TaskModelHost.Instance.RequestPass(Dispatcher);

        bool ITaskModelPanel.IsLoaded => isLoaded;
        AppBarEdge ITaskModelPanel.Edge => HostEdge;
        Func<object, bool> ITaskModelPanel.Filter => Tasks_Filter;
        Tasks ITaskModelPanel.Tasks => Tasks;
        IReadOnlyDictionary<object, string> ITaskModelPanel.PreviousDisplayKeys => displayKeys;

        void ITaskModelPanel.Apply(TaskModelEdgeResult result)
        {
            // Stored per desktop: switching lists must not leak the old desktop's ordering keys.
            desktopKeys[desktopLists.CurrentId] = result.DisplayKeys;
            TaskListDiff.Reconcile(displayedTasks, result.Items);
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
