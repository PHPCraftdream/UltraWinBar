using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ManagedShell.WindowsTasks;
using UltraWinBar.Utilities;

// Task #126: TaskOrderIdentifier.GetLegacy used to rescan the entire task source once per window
// (O(W^2) when many windows are new at once, e.g. startup or an Explorer restart).
// GetLegacyBatch computes the same result for every window in one pass. These checks build real
// ApplicationWindow instances (real NativeWindow handles, pre-seeded backing fields, so property
// reads never touch native APIs beyond the handle itself) and compare GetLegacyBatch against the
// original per-window GetLegacy on crafted sets covering every edge case GetLegacy has: mixed
// apps, non-ShowInTaskbar siblings, missing app identifiers, a window absent from the source
// collection entirely, and duplicate entries of the same window reference.
internal static class LegacyOrdinalBatchChecks
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly List<NativeWindow> created = new();

    private static ApplicationWindow CreateWindow(TasksService service, string winFileName, bool showInTaskbar)
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
        type.GetField("_showInTaskbar", NonPublicInstance).SetValue(window, (bool?)showInTaskbar);
        return window;
    }

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        try { RunChecks(); }
        finally
        {
            foreach (var window in created) window.DestroyHandle();
            created.Clear();
        }
    }

    private static void AssertBatchMatchesPerWindow(IEnumerable<ApplicationWindow> query, Tasks tasks, string context)
    {
        var windows = query.ToList();
        var expected = windows.ToDictionary(w => w, w => TaskOrderIdentifier.GetLegacy(w, tasks));
        var actual = TaskOrderIdentifier.GetLegacyBatch(windows, tasks);
        foreach (var window in windows)
        {
            if (!actual.TryGetValue(window, out string value) || value != expected[window])
                throw new Exception($"{context}: GetLegacyBatch ({value ?? "<missing>"}) diverged from GetLegacy ({expected[window] ?? "<null>"}).");
        }
    }

    private static void RunChecks()
    {
        var service = new TasksService();

        // Mixed apps, some ShowInTaskbar, some not, some missing an app identifier entirely.
        var a1 = CreateWindow(service, @"C:\apps\a.exe", showInTaskbar: true);
        var a2NotShown = CreateWindow(service, @"C:\apps\a.exe", showInTaskbar: false);
        var a3 = CreateWindow(service, @"C:\apps\a.exe", showInTaskbar: true);
        var b1 = CreateWindow(service, @"C:\apps\b.exe", showInTaskbar: true);
        var noAppId = CreateWindow(service, "", showInTaskbar: true);
        foreach (var w in new[] { a1, a2NotShown, a3, b1, noAppId }) service.Windows.Add(w);
        var tasks = new Tasks(service);

        AssertBatchMatchesPerWindow(new[] { a1, a2NotShown, a3, b1, noAppId }, tasks, "Mixed apps with non-ShowInTaskbar siblings and a missing app identifier");
        // a2NotShown does not count toward any ordinal and never breaks the scan for itself:
        // GetLegacy(a2NotShown) must equal appId + total ShowInTaskbar siblings for that appId (2).
        if (TaskOrderIdentifier.GetLegacy(a2NotShown, tasks) != @"exe:C:\apps\a.exe#2")
            throw new Exception("A non-ShowInTaskbar window must resolve to its app's total ShowInTaskbar sibling count, not its own position.");
        if (TaskOrderIdentifier.GetLegacy(noAppId, tasks) != TaskAssignmentManager.GetLegacyWindowIdentifier(noAppId))
            throw new Exception("A window with no app identifier must fall back to GetLegacyWindowIdentifier.");
        Console.WriteLine("PASS: GetLegacyBatch matches per-window GetLegacy for mixed apps, non-ShowInTaskbar siblings, and a missing app identifier.");

        // A window never added to the source collection at all: ordinal must be the app's total
        // ShowInTaskbar count, exactly like a non-ShowInTaskbar sibling.
        var absent = CreateWindow(service, @"C:\apps\a.exe", showInTaskbar: true);
        AssertBatchMatchesPerWindow(new[] { a1, a3, absent }, tasks, "A window absent from the source collection");
        if (TaskOrderIdentifier.GetLegacy(absent, tasks) != @"exe:C:\apps\a.exe#2")
            throw new Exception("A window absent from the source collection must resolve to its app's total ShowInTaskbar sibling count.");
        Console.WriteLine("PASS: GetLegacyBatch matches per-window GetLegacy for a window absent from the source collection.");

        // Duplicate entries of the same window reference in the source collection: GetLegacy
        // breaks on the first occurrence, so GetLegacyBatch must record the first one too.
        var dupService = new TasksService();
        var dupWindow = CreateWindow(dupService, @"C:\apps\dup.exe", showInTaskbar: true);
        var otherDup = CreateWindow(dupService, @"C:\apps\dup.exe", showInTaskbar: true);
        dupService.Windows.Add(dupWindow);
        dupService.Windows.Add(otherDup);
        dupService.Windows.Add(dupWindow); // duplicate reference
        var dupTasks = new Tasks(dupService);
        AssertBatchMatchesPerWindow(new[] { dupWindow, otherDup }, dupTasks, "Duplicate entries of the same window reference");
        if (TaskOrderIdentifier.GetLegacy(dupWindow, dupTasks) != @"exe:C:\apps\dup.exe#1")
            throw new Exception("A duplicated window reference must resolve using its first occurrence, matching GetLegacy's break-on-first-match.");
        Console.WriteLine("PASS: GetLegacyBatch matches per-window GetLegacy's first-occurrence semantics when a window reference is duplicated in the source collection.");

        // tasks == null and an unidentifiable window both fall back to GetLegacyWindowIdentifier,
        // even mixed into the same batch call as normal windows.
        var noTasksResult = TaskOrderIdentifier.GetLegacyBatch(new[] { a1, noAppId }, null);
        if (noTasksResult[a1] != TaskOrderIdentifier.GetLegacy(a1, null) || noTasksResult[noAppId] != TaskOrderIdentifier.GetLegacy(noAppId, null))
            throw new Exception("GetLegacyBatch with tasks == null must fall back to GetLegacyWindowIdentifier for every window, matching GetLegacy.");
        if (TaskOrderIdentifier.GetLegacyBatch(Array.Empty<ApplicationWindow>(), tasks).Count != 0)
            throw new Exception("GetLegacyBatch with no windows must return an empty result.");
        if (TaskOrderIdentifier.GetLegacy(null, tasks) != null)
            throw new Exception("GetLegacy for a null window must stay null.");
        Console.WriteLine("PASS: GetLegacyBatch reproduces GetLegacy's null-tasks and unidentifiable-window fallback, and handles an empty input.");

        // Randomized equivalence across larger, denser sibling sets (several apps, mixed
        // ShowInTaskbar, duplicated app identifiers), including queries for windows outside the
        // source collection.
        var random = new Random(126126);
        for (int trial = 0; trial < 60; trial++)
        {
            var trialService = new TasksService();
            int count = random.Next(1, 20);
            var poolWindows = new List<ApplicationWindow>();
            for (int i = 0; i < count; i++)
            {
                string appFile = random.Next(5) == 0 ? "" : $@"C:\apps\app{random.Next(4)}.exe";
                bool shown = random.Next(3) != 0;
                var w = CreateWindow(trialService, appFile, shown);
                poolWindows.Add(w);
                trialService.Windows.Add(w);
            }
            var trialTasks = new Tasks(trialService);
            // Query every pooled window plus a couple of freshly created, never-added windows.
            var query = new List<ApplicationWindow>(poolWindows);
            query.Add(CreateWindow(trialService, @"C:\apps\app0.exe", true));
            query.Add(CreateWindow(trialService, "", true));
            AssertBatchMatchesPerWindow(query, trialTasks, $"Randomized trial {trial}");
        }
        Console.WriteLine("PASS: GetLegacyBatch matches per-window GetLegacy across 60 randomized sibling sets with mixed apps, ShowInTaskbar states, and out-of-collection queries.");
    }
}
