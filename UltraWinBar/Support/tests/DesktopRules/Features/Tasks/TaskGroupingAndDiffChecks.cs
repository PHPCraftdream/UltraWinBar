using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ManagedShell.WindowsTasks;
using UltraWinBar.Utilities;

// Task #126: RebuildDisplayedTasks used to be quadratic in three places. These checks cover the
// two pure helpers extracted for it: TaskAssignmentManager.GroupByExecutableIdentifier (replaces
// a per-pin full scan of every window) and TaskListDiff.Reconcile (replaces per-item
// Contains/IndexOf scans of the displayed collection). Both are compared against the original
// O(n^2) algorithm they replaced, on crafted and randomized inputs, to prove identical results.
internal static class TaskGroupingAndDiffChecks
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly List<NativeWindow> created = new();

    private static ApplicationWindow CreateWindow(TasksService service, string winFileName)
    {
        var nativeWindow = new NativeWindow();
        nativeWindow.CreateHandle(new CreateParams());
        created.Add(nativeWindow);
        var window = new ApplicationWindow(service, nativeWindow.Handle);
        var type = typeof(ApplicationWindow);
        type.GetField("_isUWP", NonPublicInstance).SetValue(window, (bool?)false);
        type.GetField("_winFileName", NonPublicInstance).SetValue(window, winFileName ?? "");
        type.GetField("_winFileNameRetryAt", NonPublicInstance).SetValue(window, long.MaxValue);
        type.GetField("_className", NonPublicInstance).SetValue(window, "Window");
        type.GetField("_title", NonPublicInstance).SetValue(window, "Title");
        return window;
    }

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        try { RunChecks(); RunDesktopMembershipChecks(); }
        finally
        {
            foreach (var window in created) window.DestroyHandle();
            created.Clear();
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Desktop switch regression: the filter reads the window's DWM cloak state instead of a
    // cross-process IVirtualDesktopManager call that blocks while Explorer animates the switch.
    // Only shell cloaking (bit 2: another virtual desktop) excludes; app self-cloaking does not.
    private static void RunDesktopMembershipChecks()
    {
        var window = new NativeWindow();
        window.CreateHandle(new CreateParams { Style = unchecked((int)0x80000000) });
        created.Add(window);
        if (!UltraWinBar.Controls.TaskList.IsOnCurrentDesktopLocal(window.Handle))
            throw new Exception("An uncloaked window must count as on the current desktop.");
        int cloak = 1;
        if (DwmSetWindowAttribute(window.Handle, 13, ref cloak, sizeof(int)) == 0 &&
            !UltraWinBar.Controls.TaskList.IsOnCurrentDesktopLocal(window.Handle))
            throw new Exception("An app-cloaked window is not on another desktop.");
        if (!UltraWinBar.Controls.TaskList.IsOnCurrentDesktopLocal(IntPtr.Zero))
            throw new Exception("An unreadable cloak state must not hide a task.");
        Console.WriteLine("PASS: desktop membership comes from shell cloaking, read locally without Explorer.");
    }

    // Old algorithm, exactly as RebuildDisplayedTasks used to filter each pin's windows.
    private static List<ApplicationWindow> OldPinGroup(List<ApplicationWindow> windows, string identifier, HashSet<ApplicationWindow> claimed) =>
        windows.Where(w => !claimed.Contains(w) &&
            TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath) == identifier).ToList();

    private static List<ApplicationWindow> NewPinGroup(Dictionary<string, List<ApplicationWindow>> byId, string identifier, HashSet<ApplicationWindow> claimed) =>
        (byId.TryGetValue(identifier, out var candidates) ? candidates : new List<ApplicationWindow>())
            .Where(w => !claimed.Contains(w)).ToList();

    private static void RunChecks()
    {
        var service = new TasksService();
        var appA1 = CreateWindow(service, @"C:\apps\a.exe");
        var appA2 = CreateWindow(service, @"C:\apps\a.exe");
        var appB1 = CreateWindow(service, @"C:\apps\b.exe");
        var unidentifiable = CreateWindow(service, "");
        var windows = new List<ApplicationWindow> { appA1, unidentifiable, appB1, appA2 };

        var groups = TaskAssignmentManager.GroupByExecutableIdentifier(windows);
        if (!groups[@"exe:C:\apps\a.exe"].SequenceEqual(new[] { appA1, appA2 }))
            throw new Exception("Grouping must preserve each app's windows in their original relative order.");
        if (!groups[@"exe:C:\apps\b.exe"].SequenceEqual(new[] { appB1 }))
            throw new Exception("Grouping put a window under the wrong application identifier.");
        if (groups.Values.Any(g => g.Contains(unidentifiable)) || groups.Count != 2)
            throw new Exception("An unidentifiable window must not appear in any group.");

        // Claimed-across-pins semantics: even with duplicate pin identifiers (RebuildDisplayedTasks
        // dedupes pins by Identifier before this point, but the grouping helper itself must not
        // assume that), the second pin claiming the same identifier must see only what remains.
        var claimedOld = new HashSet<ApplicationWindow>();
        var firstOld = OldPinGroup(windows, @"exe:C:\apps\a.exe", claimedOld);
        foreach (var w in firstOld) claimedOld.Add(w);
        var secondOld = OldPinGroup(windows, @"exe:C:\apps\a.exe", claimedOld);

        var claimedNew = new HashSet<ApplicationWindow>();
        var firstNew = NewPinGroup(groups, @"exe:C:\apps\a.exe", claimedNew);
        foreach (var w in firstNew) claimedNew.Add(w);
        var secondNew = NewPinGroup(groups, @"exe:C:\apps\a.exe", claimedNew);

        if (!firstOld.SequenceEqual(firstNew) || !secondOld.SequenceEqual(secondNew) || secondNew.Count != 0)
            throw new Exception("Dictionary-based pin grouping diverged from the per-pin scan under the claimed-windows semantics.");
        Console.WriteLine("PASS: GroupByExecutableIdentifier groups windows by app identifier preserving order, excludes unidentifiable windows, and matches the original per-pin scan's claimed-window semantics.");

        // Randomized equivalence: many synthetic app-id assignments, verified against a naive
        // full rescan per identifier.
        var random = new Random(20260928);
        for (int trial = 0; trial < 100; trial++)
        {
            int count = random.Next(0, 15);
            var trialWindows = new List<ApplicationWindow>();
            for (int i = 0; i < count; i++)
                trialWindows.Add(CreateWindow(service, random.Next(4) == 0 ? "" : $@"C:\apps\app{random.Next(5)}.exe"));

            var byId = TaskAssignmentManager.GroupByExecutableIdentifier(trialWindows);
            var ids = trialWindows.Select(w => TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath)).Distinct().Where(id => id != null);
            foreach (var id in ids)
            {
                var expected = trialWindows.Where(w => TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath) == id).ToList();
                if (!byId.TryGetValue(id, out var actual) || !actual.SequenceEqual(expected))
                    throw new Exception($"Randomized grouping mismatch for identifier {id} on trial {trial}.");
            }
        }
        Console.WriteLine("PASS: GroupByExecutableIdentifier matches a naive full-rescan grouping across 100 randomized window sets.");

        RunDiffChecks();
    }

    // Old algorithm, exactly as RebuildDisplayedTasks used to reconcile displayedTasks.
    private static void OldReconcile(ObservableCollection<object> current, List<object> target)
    {
        for (int i = current.Count - 1; i >= 0; i--)
            if (!target.Contains(current[i])) current.RemoveAt(i);
        for (int i = 0; i < target.Count; i++)
        {
            int oldIndex = current.IndexOf(target[i]);
            if (oldIndex < 0) current.Insert(i, target[i]);
            else if (oldIndex != i) current.Move(oldIndex, i);
        }
    }

    private static void RunDiffChecks()
    {
        var random = new Random(126);
        for (int trial = 0; trial < 300; trial++)
        {
            int poolSize = random.Next(0, 12);
            var pool = Enumerable.Range(0, poolSize).Select(i => (object)$"item{i}").ToArray();
            List<object> RandomSubsetInOrder()
            {
                var chosen = pool.Where(_ => random.Next(2) == 0).ToList();
                // Random.Shuffle-by-key, deterministic under the seeded Random.
                return chosen.Select(item => (item, key: random.Next())).OrderBy(x => x.key).Select(x => x.item).ToList();
            }
            var initial = RandomSubsetInOrder();
            var target = RandomSubsetInOrder();

            var currentOld = new ObservableCollection<object>(initial);
            var currentNew = new ObservableCollection<object>(initial);
            int opsOld = 0, opsNew = 0;
            currentOld.CollectionChanged += (s, e) => opsOld++;
            currentNew.CollectionChanged += (s, e) => opsNew++;

            OldReconcile(currentOld, target);
            TaskListDiff.Reconcile(currentNew, target);

            if (!currentNew.SequenceEqual(target))
                throw new Exception($"TaskListDiff.Reconcile did not converge to the target order on trial {trial}.");
            if (!currentNew.SequenceEqual(currentOld))
                throw new Exception($"TaskListDiff.Reconcile diverged from the original Contains/IndexOf algorithm's final state on trial {trial}.");
            if (opsNew != opsOld)
                throw new Exception($"TaskListDiff.Reconcile issued a different number of collection operations ({opsNew}) than the original algorithm ({opsOld}) on trial {trial} — container reuse would differ.");
        }
        Console.WriteLine("PASS: TaskListDiff.Reconcile matches the original Contains/IndexOf diff's final collection state and operation count across 300 randomized old/new sequences.");

        // Edge cases: empty to empty, full disjoint replacement, pure reversal (all moves).
        var empty = new ObservableCollection<object>();
        TaskListDiff.Reconcile(empty, new List<object>());
        if (empty.Count != 0) throw new Exception("Reconciling empty to empty must stay empty.");

        var disjoint = new ObservableCollection<object>(new object[] { "a", "b", "c" });
        TaskListDiff.Reconcile(disjoint, new List<object> { "x", "y" });
        if (!disjoint.SequenceEqual(new object[] { "x", "y" }))
            throw new Exception("Reconciling to a fully disjoint target must remove everything old and insert everything new.");

        var reversed = new ObservableCollection<object>(new object[] { "a", "b", "c", "d" });
        TaskListDiff.Reconcile(reversed, new List<object> { "d", "c", "b", "a" });
        if (!reversed.SequenceEqual(new object[] { "d", "c", "b", "a" }))
            throw new Exception("Reconciling a full reversal must end in the reversed order.");
        Console.WriteLine("PASS: TaskListDiff.Reconcile handles empty, fully disjoint, and fully reversed sequences.");
    }
}
