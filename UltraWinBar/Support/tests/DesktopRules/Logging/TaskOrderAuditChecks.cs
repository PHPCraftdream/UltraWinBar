using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

// The task order audit line: written desktop vs the other desktops aligned by it, added/removed/moved keys, short labels.
internal static class TaskOrderAuditChecks
{
    private static readonly Guid A = Guid.Parse("741c8acf-b625-45e8-b44f-5a9942b873ee");
    private static readonly Guid B = Guid.Parse("96aee498-6f6a-4ae9-9b41-d30e6ab87015");

    private static List<TaskOrderEntry> Order(AppBarEdge edge, Guid scope, params string[] keys) =>
        keys.Select(key => new TaskOrderEntry { Edge = edge, DesktopId = scope, Identifier = key }).ToList();

    private static string Window(uint pid, string hwnd) => $"window:v2:{pid}:639279160433825218:{hwnd}";

    internal static void Run()
    {
        string x = Window(100, "1A"), y = Window(200, "2B"), z = Window(300, "3C");
        var bottom = AppBarEdge.Bottom;

        var same = Order(bottom, A, x, y, z);
        if (TaskOrderAudit.Describe(same, same.ToList(), bottom, A) != null)
            throw new Exception("An unchanged order must not produce a line.");

        string reordered = TaskOrderAudit.Describe(same, Order(bottom, A, z, x, y), bottom, A)
            ?? throw new Exception("A reordered scope must produce a line.");
        if (!reordered.Contains("edge=Bottom") || !reordered.Contains("written 741c8acf") || !reordered.Contains("moved=3") || !reordered.Contains("w300:3C 2>0"))
            throw new Exception("The line must name the edge, the written desktop and each moved key with its old and new place: " + reordered);

        var before = Order(bottom, A, x, y).Concat(Order(bottom, B, x, y, z)).ToList();
        var after = Order(bottom, A, x, y).Concat(Order(bottom, B, y, x, z)).ToList();
        string aligned = TaskOrderAudit.Describe(before, after, bottom, A)
            ?? throw new Exception("Aligning another desktop's order must produce a line.");
        if (!aligned.Contains("aligned 96aee498") || aligned.Contains("written"))
            throw new Exception("Another desktop touched by alignment is reported as aligned, not written: " + aligned);

        string added = TaskOrderAudit.Describe(Order(bottom, A, x), Order(bottom, A, x, Window(7740, "40E78")), bottom, A);
        if (added == null || !added.Contains("+[w7740:40E78]") || added.Contains("moved"))
            throw new Exception("An appended window is reported as added only: " + added);
        string removed = TaskOrderAudit.Describe(Order(bottom, A, x, y), Order(bottom, A, x), bottom, A);
        if (removed == null || !removed.Contains("-[w200:2B]"))
            throw new Exception("A dropped key is reported as removed: " + removed);

        if (TaskOrderAudit.Describe(Order(AppBarEdge.Top, A, x, y), Order(AppBarEdge.Top, A, y, x), bottom, A) != null)
            throw new Exception("Another edge's order is not this edge's change.");

        string capped = TaskOrderAudit.Describe(Order(bottom, A), Order(bottom, A, Enumerable.Range(1, 20).Select(i => Window((uint)i, "F")).ToArray()), bottom, A);
        if (capped == null || !capped.Contains("...12 more"))
            throw new Exception("A long list is capped: " + capped);

        if (TaskOrderAudit.Short(x) != "w100:1A" ||
            TaskOrderAudit.Short(@"pin:exe:C:\Program Files\Google\Chrome\Application\chrome.exe") != "pin:chrome.exe" ||
            TaskOrderAudit.Short(@"exe:C:\Program Files\Yandex\YandexBrowser\Application\browser.exe#3") != "browser.exe#3")
            throw new Exception("Keys are shortened to a window's pid and handle or an application's file name.");

        string pruned = TaskOrderAudit.DescribePrune(Order(bottom, A, x, y), Order(bottom, A, x));
        if (pruned == null || !pruned.Contains("Task order pruned") || !pruned.Contains("-[w200:2B]") || !pruned.Contains("741c8acf"))
            throw new Exception("Pruning reports the dropped keys per desktop: " + pruned);
        if (TaskOrderAudit.DescribePrune(same, same.ToList()) != null)
            throw new Exception("Nothing pruned, nothing logged.");
        Console.WriteLine("PASS: the task order audit names the written desktop, the desktops aligned by it, and each added, removed or moved key; long lists are capped and pruning is reported per desktop.");
    }
}
