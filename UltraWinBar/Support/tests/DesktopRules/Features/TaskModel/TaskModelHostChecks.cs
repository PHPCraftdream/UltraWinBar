using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using ManagedShell.AppBar;
using ManagedShell.WindowsTasks;
using UltraWinBar.Utilities;

// Task #128: TaskModelHost coordinates every registered TaskList panel through one TaskModel.Compute
// pass per dispatcher tick. These checks exercise the coordinator itself (registration lifetime,
// pass coalescing, Settings write convergence) against a real background-thread Dispatcher — the
// same "own STA thread + Dispatcher.Run()" pattern NativeCallbackChecks.cs uses — with lightweight
// fake ITaskModelPanel panels instead of a real TaskList (which needs XAML/Taskbar/Host wiring this
// suite doesn't want to depend on). TaskModel.Compute's own semantics (ordering/pins/pruning,
// including multi-panel-same-edge chaining) are covered exhaustively in TaskModelEquivalenceChecks;
// this suite is about the coordinator's scheduling and Settings-application contract around it.
internal static class TaskModelHostChecks
{
    private sealed class FakePanel : ITaskModelPanel
    {
        public bool IsLoaded { get; set; } = true;
        public AppBarEdge Edge { get; set; }
        public Func<object, bool> Filter { get; set; } = _ => true;
        public Tasks Tasks { get; set; }
        public IReadOnlyDictionary<object, string> PreviousDisplayKeys { get; set; } = new Dictionary<object, string>();
        public int ApplyCount { get; private set; }
        public TaskModelEdgeResult LastResult { get; private set; }

        public void Apply(TaskModelEdgeResult result)
        {
            ApplyCount++;
            LastResult = result;
            PreviousDisplayKeys = result.DisplayKeys;
        }
    }

    // Runs 'body' on its own STA thread's Dispatcher (TaskModelHost.RequestPass needs a real
    // Dispatcher to schedule Background-priority work on).
    private static void WithDispatcher(Action<Dispatcher> body)
    {
        Dispatcher dispatcher = null;
        var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        try { dispatcher.Invoke(() => body(dispatcher)); }
        finally { dispatcher.InvokeShutdown(); thread.Join(); }
    }

    // Background is a higher dispatcher priority than ContextIdle, so this blocks until every
    // Background-priority item queued so far (i.e. any pending TaskModelHost pass) has run.
    private static void Pump(Dispatcher dispatcher) => dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static ApplicationWindow CreateWindow(TasksService service, NativeWindow native, string winFileName)
    {
        var window = new ApplicationWindow(service, native.Handle);
        var type = typeof(ApplicationWindow);
        const System.Reflection.BindingFlags nonPublicInstance = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        type.GetField("_isUWP", nonPublicInstance).SetValue(window, (bool?)false);
        type.GetField("_winFileName", nonPublicInstance).SetValue(window, winFileName);
        type.GetField("_winFileNameRetryAt", nonPublicInstance).SetValue(window, long.MaxValue);
        type.GetField("_className", nonPublicInstance).SetValue(window, "Window");
        type.GetField("_title", nonPublicInstance).SetValue(window, "Title");
        type.GetField("_showInTaskbar", nonPublicInstance).SetValue(window, (bool?)true);
        return window;
    }

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunCoalescingChecks();
        RunNoOpConvergenceChecks();
        RunUnregisterAndGcChecks();
        RunMultiPanelSameEdgeRegressionChecks();
    }

    private static void RunCoalescingChecks()
    {
        WithDispatcher(dispatcher =>
        {
            var panels = new List<FakePanel>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var panel = new FakePanel { Edge = AppBarEdge.Bottom };
                    panels.Add(panel);
                    TaskModelHost.Instance.Register(panel);
                }
                int before = TaskModelHost.Instance.ComputeCount;

                // Several triggers, from several panels, all before the pass actually runs.
                TaskModelHost.Instance.RequestPass(dispatcher);
                TaskModelHost.Instance.RequestPass(dispatcher);
                TaskModelHost.Instance.RequestPass(dispatcher);
                Pump(dispatcher);
                if (TaskModelHost.Instance.ComputeCount != before + 1)
                    throw new Exception("Several RequestPass calls before the pass runs must coalesce into exactly one Compute.");
                if (panels.Any(p => p.ApplyCount != 1))
                    throw new Exception("Every registered panel must receive exactly one Apply call per coalesced pass.");

                // A fresh request after the previous pass completed must schedule a new one.
                TaskModelHost.Instance.RequestPass(dispatcher);
                Pump(dispatcher);
                if (TaskModelHost.Instance.ComputeCount != before + 2)
                    throw new Exception("A RequestPass after the previous pass completed must schedule a new pass.");
                if (panels.Any(p => p.ApplyCount != 2))
                    throw new Exception("The second pass must apply to every panel again.");

                // Desktop switch: an urgent (Send) request overtakes a pending Background one and still runs once.
                bool ranBeforeNormalWork = false;
                int urgentBefore = TaskModelHost.Instance.ComputeCount;
                dispatcher.Invoke(() =>
                {
                    TaskModelHost.Instance.RequestPass(dispatcher);
                    TaskModelHost.Instance.RequestPass(dispatcher, DispatcherPriority.Send);
                    dispatcher.BeginInvoke(new Action(() =>
                        ranBeforeNormalWork = TaskModelHost.Instance.ComputeCount == urgentBefore + 1), DispatcherPriority.Normal);
                });
                Pump(dispatcher);
                if (!ranBeforeNormalWork || TaskModelHost.Instance.ComputeCount != urgentBefore + 1)
                    throw new Exception("An urgent RequestPass must run before Normal work and coalesce with the pending Background pass.");
            }
            finally { foreach (var panel in panels) TaskModelHost.Instance.Unregister(panel); }
        });
        Console.WriteLine("PASS: TaskModelHost coalesces any number of RequestPass calls across several panels into exactly one Compute pass, and schedules a fresh pass afterward; an urgent request overtakes a pending one.");
    }

    private static void RunNoOpConvergenceChecks()
    {
        WithDispatcher(dispatcher =>
        {
            var service = new TasksService();
            var native = new NativeWindow();
            native.CreateHandle(new CreateParams());
            try
            {
                var window = CreateWindow(service, native, @"C:\apps\conv.exe");
                service.Windows.Add(window);
                var tasks = new Tasks(service);

                var originalTaskOrder = Settings.Instance.TaskOrder;
                var originalAssignments = Settings.Instance.TaskbarAssignments;
                var originalPins = Settings.Instance.PinnedApplications;
                var pin = new PinnedApplication { Edge = AppBarEdge.Bottom, Identifier = @"exe:C:\apps\conv.exe", PrimaryWindowKey = "window:v2:dead:0:0", DesktopId = Guid.Empty };
                try
                {
                    Settings.Instance.TaskOrder = new List<TaskOrderEntry>();
                    Settings.Instance.TaskbarAssignments = new List<TaskbarAssignment>();
                    Settings.Instance.PinnedApplications = new List<PinnedApplication> { pin };

                    var panel = new FakePanel { Edge = AppBarEdge.Bottom, Tasks = tasks };
                    TaskModelHost.Instance.Register(panel);
                    try
                    {
                        TaskModelHost.Instance.RequestPass(dispatcher);
                        Pump(dispatcher);
                        if (panel.ApplyCount != 1) throw new Exception("The first pass must apply exactly once.");
                        if (pin.PrimaryWindowKey == "window:v2:dead:0:0")
                            throw new Exception("The first pass must reconcile the pin's dead PrimaryWindowKey to the discovered live window.");

                        int taskOrderChanges = 0, assignmentChanges = 0, pinChanges = 0;
                        PropertyChangedEventHandler handler = (s, e) =>
                        {
                            if (e.PropertyName == nameof(Settings.TaskOrder)) taskOrderChanges++;
                            if (e.PropertyName == nameof(Settings.TaskbarAssignments)) assignmentChanges++;
                            if (e.PropertyName == nameof(Settings.PinnedApplications)) pinChanges++;
                        };
                        Settings.Instance.PropertyChanged += handler;
                        try
                        {
                            // Second pass: identical windows/pins/order as the first pass left
                            // behind, so nothing should change.
                            TaskModelHost.Instance.RequestPass(dispatcher);
                            Pump(dispatcher);
                        }
                        finally { Settings.Instance.PropertyChanged -= handler; }

                        if (panel.ApplyCount != 2) throw new Exception("A second pass must still hand the panel its (unchanged) result.");
                        if (taskOrderChanges != 0 || assignmentChanges != 0 || pinChanges != 0)
                            throw new Exception($"A second pass with nothing changed must write nothing to Settings (TaskOrder={taskOrderChanges}, TaskbarAssignments={assignmentChanges}, PinnedApplications={pinChanges}).");

                        // A third pass proves the PinnedApplications write-back genuinely converged:
                        // PinnedApplications is itself a rebuild trigger (TaskList.Settings_PropertyChanged),
                        // so an oscillating reconciliation would keep re-triggering passes/writes forever.
                        TaskModelHost.Instance.RequestPass(dispatcher);
                        Pump(dispatcher);
                        if (panel.ApplyCount != 3) throw new Exception("A third pass must still complete (no deadlock/requeue loop).");
                    }
                    finally { TaskModelHost.Instance.Unregister(panel); }
                }
                finally
                {
                    Settings.Instance.TaskOrder = originalTaskOrder;
                    Settings.Instance.TaskbarAssignments = originalAssignments;
                    Settings.Instance.PinnedApplications = originalPins;
                }
            }
            finally { native.DestroyHandle(); }
        });
        Console.WriteLine("PASS: a pass with nothing changed writes nothing to Settings (TaskOrder/TaskbarAssignments/PinnedApplications), and the PinnedApplications reconciliation write-back converges instead of endlessly requeuing.");
    }

    private static void RunUnregisterAndGcChecks()
    {
        WithDispatcher(dispatcher =>
        {
            FakePanel stableOtherPanel = null;
            try
            {
                WeakReference weak = CreateAndRegisterDisposablePanel(dispatcher, out stableOtherPanel);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (weak.IsAlive)
                    throw new Exception("A registered panel with no other strong reference must be collectible (registration is by WeakReference).");

                var explicitPanel = new FakePanel { Edge = AppBarEdge.Bottom };
                TaskModelHost.Instance.Register(explicitPanel);
                TaskModelHost.Instance.RequestPass(dispatcher);
                Pump(dispatcher);
                int afterFirst = explicitPanel.ApplyCount;
                if (afterFirst == 0) throw new Exception("A registered panel must be applied at least once.");

                TaskModelHost.Instance.Unregister(explicitPanel);
                TaskModelHost.Instance.RequestPass(dispatcher);
                Pump(dispatcher);
                if (explicitPanel.ApplyCount != afterFirst)
                    throw new Exception("An unregistered panel must not be part of any later pass.");
            }
            finally { if (stableOtherPanel != null) TaskModelHost.Instance.Unregister(stableOtherPanel); }
        });
        Console.WriteLine("PASS: an explicitly unregistered panel receives no further passes; a panel with no other reference is collectible even without unregistering.");
    }

    // Registers a throwaway panel (no reference kept by the caller) and an unrelated stable panel
    // (kept alive by the caller, so RequestPass has a live panel and a pass actually runs), then
    // returns a WeakReference to the throwaway one.
    private static WeakReference CreateAndRegisterDisposablePanel(Dispatcher dispatcher, out FakePanel stableOtherPanel)
    {
        var other = new FakePanel { Edge = AppBarEdge.Bottom };
        TaskModelHost.Instance.Register(other);
        var throwaway = new FakePanel { Edge = AppBarEdge.Bottom };
        var weak = new WeakReference(throwaway);
        TaskModelHost.Instance.Register(throwaway);
        TaskModelHost.Instance.RequestPass(dispatcher);
        Pump(dispatcher);
        stableOtherPanel = other;
        return weak;
    }

    private static void RunMultiPanelSameEdgeRegressionChecks()
    {
        WithDispatcher(dispatcher =>
        {
            var service = new TasksService();
            var native1 = new NativeWindow();
            var native2 = new NativeWindow();
            native1.CreateHandle(new CreateParams());
            native2.CreateHandle(new CreateParams());
            try
            {
                var m1 = CreateWindow(service, native1, @"C:\apps\multi.exe");
                var m2 = CreateWindow(service, native2, @"C:\apps\multi.exe");
                service.Windows.Add(m1);
                service.Windows.Add(m2);
                var tasks = new Tasks(service);

                var originalTaskOrder = Settings.Instance.TaskOrder;
                var originalAssignments = Settings.Instance.TaskbarAssignments;
                var originalPins = Settings.Instance.PinnedApplications;
                var pin = new PinnedApplication { Edge = AppBarEdge.Bottom, Identifier = @"exe:C:\apps\multi.exe", PrimaryWindowKey = "window:v2:dead:0:0", DesktopId = Guid.Empty };
                try
                {
                    Settings.Instance.TaskOrder = new List<TaskOrderEntry>();
                    Settings.Instance.TaskbarAssignments = new List<TaskbarAssignment>();
                    Settings.Instance.PinnedApplications = new List<PinnedApplication> { pin };

                    // Two panels on the same edge (e.g. two monitors under Settings.ShowMultiMon),
                    // each seeing only its own monitor's window, registered in this order.
                    var monitor1 = new FakePanel { Edge = AppBarEdge.Bottom, Tasks = tasks, Filter = w => ReferenceEquals(w, m1) };
                    var monitor2 = new FakePanel { Edge = AppBarEdge.Bottom, Tasks = tasks, Filter = w => ReferenceEquals(w, m2) };
                    TaskModelHost.Instance.Register(monitor1);
                    TaskModelHost.Instance.Register(monitor2);
                    try
                    {
                        TaskModelHost.Instance.RequestPass(dispatcher);
                        Pump(dispatcher);

                        if (!monitor1.LastResult.Items.SequenceEqual(new object[] { m1 }))
                            throw new Exception("The first same-edge panel must see only its own monitor's window.");
                        if (!monitor2.LastResult.Items.SequenceEqual(new object[] { m2 }))
                            throw new Exception("The second same-edge panel must see only its own monitor's window.");

                        // The pin was claimed by the first panel processed (monitor1): its window
                        // is the reconciled PrimaryWindowKey, and it must not flip when monitor2
                        // (which also has a live candidate for the same app) runs right after.
                        string expectedKey = TaskOrderIdentifier.Get(m1, tasks);
                        if (pin.PrimaryWindowKey != expectedKey)
                            throw new Exception("The pin must converge on the first same-edge panel's window and not flip to the second panel's.");

                        // The edge's persisted order (what the second, later panel leaves behind)
                        // must be exactly what Settings now has for this edge.
                        List<string> persisted = Settings.Instance.GetTaskOrderForEdge(AppBarEdge.Bottom, Guid.Empty);
                        if (!persisted.SequenceEqual(monitor2.LastResult.SavedOrder))
                            throw new Exception("The edge's persisted TaskOrder must equal the last same-edge panel's SavedOrder.");
                    }
                    finally
                    {
                        TaskModelHost.Instance.Unregister(monitor1);
                        TaskModelHost.Instance.Unregister(monitor2);
                    }
                }
                finally
                {
                    Settings.Instance.TaskOrder = originalTaskOrder;
                    Settings.Instance.TaskbarAssignments = originalAssignments;
                    Settings.Instance.PinnedApplications = originalPins;
                }
            }
            finally { native1.DestroyHandle(); native2.DestroyHandle(); }
        });
        Console.WriteLine("PASS: two panels registered on the same edge (multi-monitor) each get only their own monitor's windows, chain their persisted order, and a shared pin converges on the first panel's window without flipping.");
    }
}
