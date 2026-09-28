using ManagedShell.AppBar;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// One TaskList panel's contract with TaskModelHost: what one Compute() call needs from it,
    /// and where its slice of the result goes. Deliberately free of WPF UserControl/Dispatcher
    /// details beyond Edge/IsLoaded, so a test double can stand in for a real panel.
    /// </summary>
    internal interface ITaskModelPanel
    {
        bool IsLoaded { get; }
        AppBarEdge Edge { get; }

        /// <summary>This panel's Tasks_Filter, applied to each window. Every live panel is expected
        /// to share the same Tasks/window source (see ShellManager.Tasks) — TaskModelHost reads the
        /// source collection from whichever panel happens to be processed first.</summary>
        Func<object, bool> Filter { get; }

        Tasks Tasks { get; }
        IReadOnlyDictionary<object, string> PreviousDisplayKeys { get; }

        void Apply(TaskModelEdgeResult result);
    }

    /// <summary>
    /// Coordinates every registered TaskList panel through one TaskModel.Compute pass per
    /// dispatcher tick (#128), instead of each panel recomputing LiveKeys/pruning/order and
    /// writing to Settings on its own. Panels register on load (TaskList.SetTasksCollection) and
    /// unregister on unload (TaskList_OnUnloaded); registration is by WeakReference too, so a
    /// panel that misses unregistering is still collectible, never kept alive by this singleton.
    /// </summary>
    internal sealed class TaskModelHost
    {
        internal static readonly TaskModelHost Instance = new();

        private readonly List<WeakReference<ITaskModelPanel>> panels = new();
        private bool passPending;

        /// <summary>Number of completed Compute passes. Test-only instrumentation for asserting
        /// that several triggers across several panels coalesce into one pass.</summary>
        internal int ComputeCount { get; private set; }

        internal void Register(ITaskModelPanel panel) => panels.Add(new WeakReference<ITaskModelPanel>(panel));

        internal void Unregister(ITaskModelPanel panel) =>
            panels.RemoveAll(w => !w.TryGetTarget(out var target) || ReferenceEquals(target, panel));

        /// <summary>Schedules one Compute pass covering every registered panel, coalescing any
        /// number of calls (from any number of panels) made before the pass actually runs into a
        /// single Background-priority dispatch — the same coalescing QueueTaskRebuild used to do
        /// per panel, now shared across all of them.</summary>
        internal void RequestPass(Dispatcher dispatcher)
        {
            if (passPending) return;
            passPending = true;
            dispatcher.BeginInvoke(new Action(RunPass), DispatcherPriority.Background);
        }

        private void RunPass()
        {
            passPending = false;

            var live = new List<ITaskModelPanel>();
            for (int i = panels.Count - 1; i >= 0; i--)
            {
                if (!panels[i].TryGetTarget(out ITaskModelPanel panel)) { panels.RemoveAt(i); continue; }
                if (panel.IsLoaded) live.Add(panel);
            }
            if (live.Count == 0) return;
            live.Reverse(); // registration order (walked panels back-to-front above)
            ComputeCount++;

            Tasks tasks = live.Select(p => p.Tasks).FirstOrDefault(t => t != null);
            List<ApplicationWindow> windows = tasks?.GroupedWindows?.SourceCollection?.Cast<object>().OfType<ApplicationWindow>().ToList()
                ?? new List<ApplicationWindow>();
            Dictionary<ApplicationWindow, string> legacyKeys = TaskOrderIdentifier.GetLegacyBatch(windows, tasks);
            var windowSnapshots = windows.Select(w => new TaskWindowSnapshot
            {
                Window = w,
                Key = TaskOrderIdentifier.Get(w, tasks),
                FallbackKey = "hwnd:" + w.Handle,
                LegacyKey = legacyKeys[w],
                AppIdentifier = TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath),
            }).ToList();

            List<PinnedApplication> pinnedApplications = Settings.Instance.PinnedApplications;
            var pinSnapshots = pinnedApplications.Select(p => new PinSnapshot
            {
                Pin = p,
                Edge = p.Edge,
                Identifier = p.Identifier,
                PrimaryWindowKey = p.PrimaryWindowKey,
                OrderKey = p.OrderKey,
                OnCurrentDesktop = p.OnCurrentDesktop,
            }).ToList();

            var panelRequests = live.Select(p => new TaskPanelRequest
            {
                PanelId = p,
                Edge = p.Edge,
                Filter = p.Filter,
                PreviousDisplayKeys = p.PreviousDisplayKeys,
            }).ToList();

            Guid desktopId = VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty;
            var input = new TaskModelInput
            {
                Windows = windowSnapshots,
                Pins = pinSnapshots,
                TaskOrder = Settings.Instance.TaskOrder,
                TaskbarAssignments = Settings.Instance.TaskbarAssignments,
                Panels = panelRequests,
                CurrentDesktopId = desktopId,
                WindowStillExists = TaskOrderIdentifier.WindowStillExists,
            };

            TaskModelResult result = TaskModel.Compute(input);

            // Settings writes, once, outside any panel's view update.
            if (result.PrunedTaskOrder != null) Settings.Instance.TaskOrder = result.PrunedTaskOrder;
            if (result.PrunedTaskbarAssignments != null) Settings.Instance.TaskbarAssignments = result.PrunedTaskbarAssignments;
            foreach (var edgeOrder in result.SavedOrders)
                Settings.Instance.SetTaskOrderForEdge(edgeOrder.Key, edgeOrder.Value, desktopId);
            if (result.PinPrimaryWindowKeyChanges.Count > 0)
            {
                foreach (var change in result.PinPrimaryWindowKeyChanges)
                    ((PinnedApplication)change.Pin).PrimaryWindowKey = change.NewPrimaryWindowKey;
                Settings.Instance.PinnedApplications = Settings.Instance.PinnedApplications.ToList();
            }

            foreach (var panel in live)
                if (result.Panels.TryGetValue(panel, out TaskModelEdgeResult panelResult))
                    panel.Apply(panelResult);
        }
    }
}
