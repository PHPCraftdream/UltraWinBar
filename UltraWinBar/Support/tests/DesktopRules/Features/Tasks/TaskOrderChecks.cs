using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class TaskOrderChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        var desktopA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var desktopB = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var defaults = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(
            Activator.CreateInstance(typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings")),
            typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings")));
        var windowKeys = new[] { TaskOrderIdentifier.CreateKey(100, 200, 300),
            TaskOrderIdentifier.CreateKey(100, 200, 301), TaskOrderIdentifier.CreateKey(100, 200, 302) };
        var desired = new[] { windowKeys[2], windowKeys[0], windowKeys[1] };
        foreach (var discovery in new[] { windowKeys, windowKeys.Reverse().ToArray(), new[] { windowKeys[1], windowKeys[2], windowKeys[0] } })
        {
            var restored = discovery.OrderBy(key => Array.IndexOf(desired, key));
            if (!restored.SequenceEqual(desired)) throw new Exception("Discovery order changed restored order.");
        }
        if (TaskOrderIdentifier.CreateKey(100, 201, 300) == windowKeys[0])
            throw new Exception("Reused process/window handle shares an order identity.");
        Console.WriteLine("PASS: window identities restore identical order across discovery permutations and distinguish process lifetimes.");

        var pruneEntriesMethod = typeof(TaskOrderIdentifier).GetMethod("PruneDeadWindowEntries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var pruneAssignmentsMethod = typeof(TaskOrderIdentifier).GetMethod("PruneDeadWindowAssignments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var reconcileMethod = typeof(TaskOrderIdentifier).GetMethod("ReconcilePrimaryWindowKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (pruneEntriesMethod == null || pruneAssignmentsMethod == null || reconcileMethod == null)
            throw new Exception("TaskOrder maintenance functions are missing.");
        string liveWindowA = TaskOrderIdentifier.CreateKey(1, 100, 10);
        string liveWindowB = TaskOrderIdentifier.CreateKey(2, 200, 20);
        string closedWindow = TaskOrderIdentifier.CreateKey(3, 300, 30);
        var liveWindowKeys = new HashSet<string> { liveWindowA, liveWindowB };
        var taskOrderEntries = new List<TaskOrderEntry>
        {
            new() { Edge = AppBarEdge.Bottom, DesktopId = desktopA, Identifier = "pin:exe:browser" },
            new() { Edge = AppBarEdge.Bottom, DesktopId = desktopA, Identifier = closedWindow },
            new() { Edge = AppBarEdge.Left, DesktopId = desktopB, Identifier = liveWindowA },
            new() { Edge = AppBarEdge.Left, DesktopId = desktopB, Identifier = "exe:app.exe#2" },
            new() { Edge = AppBarEdge.Top, DesktopId = Guid.Empty, Identifier = closedWindow },
            new() { Edge = AppBarEdge.Top, DesktopId = Guid.Empty, Identifier = liveWindowB },
            new() { Edge = AppBarEdge.Right, DesktopId = desktopA, Identifier = "class:Notepad|title:Untitled" },
        };
        var prunedTaskOrder = (List<TaskOrderEntry>)pruneEntriesMethod.Invoke(null, new object[] { taskOrderEntries, liveWindowKeys });
        if (prunedTaskOrder == null) throw new Exception("Dead window TaskOrder entries across multiple scopes were not pruned.");
        if (!prunedTaskOrder.Select(entry => entry.Identifier).SequenceEqual(
            new[] { "pin:exe:browser", liveWindowA, "exe:app.exe#2", liveWindowB, "class:Notepad|title:Untitled" }))
            throw new Exception("TaskOrder pruning changed relative order or dropped a pin/legacy/exe/live-window key.");
        if (!prunedTaskOrder.Any(entry => entry.Edge == AppBarEdge.Left && entry.DesktopId == desktopB && entry.Identifier == liveWindowA) ||
            !prunedTaskOrder.Any(entry => entry.Edge == AppBarEdge.Top && entry.DesktopId == Guid.Empty && entry.Identifier == liveWindowB))
            throw new Exception("TaskOrder pruning must remove dead window keys across every edge/desktop scope at once.");
        if (pruneEntriesMethod.Invoke(null, new object[] { prunedTaskOrder, liveWindowKeys }) != null)
            throw new Exception("Re-pruning an already-clean TaskOrder list must be a no-op.");
        var taskbarAssignments = new List<TaskbarAssignment>
        {
            new() { Identifier = "exe:app.exe", Mode = TaskAssignmentMode.ExecutablePath, Edge = AppBarEdge.Bottom, DesktopId = desktopA },
            new() { Identifier = closedWindow, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Left, DesktopId = desktopA },
            new() { Identifier = liveWindowA, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Right, DesktopId = desktopB },
            new() { Identifier = "class:Notepad|title:Untitled", Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Top, DesktopId = Guid.Empty },
            new() { Identifier = liveWindowB, Mode = TaskAssignmentMode.WindowClassAndTitle, Edge = AppBarEdge.Bottom, DesktopId = desktopB },
        };
        var prunedAssignments = (List<TaskbarAssignment>)pruneAssignmentsMethod.Invoke(null, new object[] { taskbarAssignments, liveWindowKeys });
        if (prunedAssignments == null) throw new Exception("Dead per-window TaskbarAssignments were not pruned.");
        if (!prunedAssignments.Select(assignment => assignment.Identifier).SequenceEqual(
            new[] { "exe:app.exe", liveWindowA, "class:Notepad|title:Untitled", liveWindowB }))
            throw new Exception("TaskbarAssignments pruning changed relative order or dropped a live-window/legacy/exe assignment.");
        if (pruneAssignmentsMethod.Invoke(null, new object[] { prunedAssignments, liveWindowKeys }) != null)
            throw new Exception("Re-pruning an already-clean TaskbarAssignments list must be a no-op.");
        string ReconcilePrimaryWindowKey(string primary, string selected, HashSet<string> live) =>
            (string)reconcileMethod.Invoke(null, new object[] { primary, selected, live });
        if (ReconcilePrimaryWindowKey(closedWindow, liveWindowA, liveWindowKeys) != liveWindowA)
            throw new Exception("A pin bound to a closed window did not adopt the newly selected live window.");
        if (ReconcilePrimaryWindowKey(null, liveWindowA, liveWindowKeys) != liveWindowA)
            throw new Exception("A pin with no recorded window did not adopt the newly selected live window.");
        if (ReconcilePrimaryWindowKey(liveWindowA, liveWindowA, liveWindowKeys) != liveWindowA)
            throw new Exception("Pin-key reconciliation is not idempotent for an already-live primary key.");
        if (ReconcilePrimaryWindowKey(liveWindowA, liveWindowB, liveWindowKeys) != liveWindowA)
            throw new Exception("Pin-key reconciliation must not flip between two live windows of the same pinned application.");
        if (ReconcilePrimaryWindowKey(closedWindow, null, liveWindowKeys) != closedWindow)
            throw new Exception("Pin-key reconciliation must not discard the recorded key when no window is currently selected.");
        var liveKeysMethod = typeof(TaskOrderIdentifier).GetMethod("LiveKeys", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        using (var undiscoveredWindow = new System.Windows.Forms.Form())
        {
            uint ownProcessId = (uint)Environment.ProcessId;
            string undiscoveredKey = TaskOrderIdentifier.CreateKey(ownProcessId, 1, undiscoveredWindow.Handle.ToInt64());
            string foreignOwnerKey = TaskOrderIdentifier.CreateKey(ownProcessId + 1, 1, undiscoveredWindow.Handle.ToInt64());
            string destroyedKey = TaskOrderIdentifier.CreateKey(ownProcessId, 1, 0x7FFFFFF0);
            var storedLiveKeys = (HashSet<string>)liveKeysMethod.Invoke(null, new object[] { null,
                new[] { undiscoveredKey, foreignOwnerKey, destroyedKey, "pin:exe:C:\\app.exe", null } });
            if (!storedLiveKeys.SetEquals(new[] { undiscoveredKey }))
                throw new Exception("Stored window keys must stay live while their window still exists, even before the task source discovers it.");
        }
        Console.WriteLine("PASS: dead window keys are pruned from every TaskOrder/TaskbarAssignments scope while pins, legacy, exe, and live-window entries keep their relative order and pruning is idempotent; pin-key reconciliation adopts a newly live window once, stays idempotent, and never flips between two live windows.");

        foreach (var example in new[] {
            (new DateTime(2016, 12, 1), "01 Kislev 5777"),
            (new DateTime(2024, 2, 10), "01 Adar I 5784"),
            (new DateTime(2024, 3, 11), "01 Adar II 5784"),
            (new DateTime(2023, 2, 22), "01 Adar 5783"),
            (new DateTime(2023, 9, 16), "01 Tishri 5784") })
            if (HebrewClockFormatter.FormatDate(example.Item1) != example.Item2)
                throw new Exception($"Hebrew date mismatch for {example.Item1:yyyy-MM-dd}");
        var dayNames = new[] { "Day-1", "Day-2", "Day-3", "Day-4", "Day-5", "Day-6", "Shabbat" };
        for (int i = 0; i < 7; i++)
            if (HebrewClockFormatter.DayName((DayOfWeek)i) != dayNames[i]) throw new Exception("Wrong weekday label.");
        if (HebrewClockFormatter.FormatDate(DateTime.MinValue) != "" || defaults["ShowHebrewDate"].GetValue<bool>())
            throw new Exception("Hebrew clock boundary/default mismatch.");
        Console.WriteLine("PASS: Hebrew dates, ordinary/leap Adar, new year, weekday labels, and disabled default.");
        if (HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "русский") != "01 кислев 5777" ||
            HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "עברית") != "01 כסלו 5777")
            throw new Exception("Localized Hebrew month mismatch.");
        var hebrewCalendar = new System.Globalization.HebrewCalendar();
        foreach (string language in HebrewClockFormatter.SupportedLanguages)
        foreach (int year in new[] { 5783, 5784 })
        for (int month = 1; month <= hebrewCalendar.GetMonthsInYear(year); month++)
        {
            var date = hebrewCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
            if (!HebrewClockFormatter.FormatDate(date, language).EndsWith(year.ToString()))
                throw new Exception($"Missing Hebrew month translation: {language}/{year}/{month}");
        }
        Console.WriteLine($"PASS: CLDR month names for {HebrewClockFormatter.SupportedLanguages.Count} UI languages in ordinary and leap years.");
        if (HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "беларуская") != "01 кіслеў 5777")
            throw new Exception("Belarusian month is not localized.");
        foreach (int year in new[] { 5783, 5784 })
        for (int month = 1; month <= hebrewCalendar.GetMonthsInYear(year); month++)
        {
            string text = HebrewClockFormatter.FormatDate(hebrewCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0), "беларуская");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\bI{1,2}\b", "");
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "[A-Za-z]")) throw new Exception("Latin Belarusian month name.");
        }
        var englishXml = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "English.xaml"));
        var belarusianXml = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "Belarusian.xaml"));
        System.Xml.Linq.XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var requiredKeys = englishXml.Root.Elements().Where(e => e.Name.LocalName is "String" or "Array")
            .Select(e => (string)e.Attribute(xamlNamespace + "Key"));
        var translatedKeys = belarusianXml.Root.Elements().Select(e => (string)e.Attribute(xamlNamespace + "Key")).ToArray();
        if (requiredKeys.Except(translatedKeys).Any()) throw new Exception("Missing Belarusian translations: " + string.Join(",", requiredKeys.Except(translatedKeys)));
        foreach (var text in belarusianXml.Descendants().Where(e => e.Name.LocalName == "String").Select(e => e.Value))
        {
            string words = System.Text.RegularExpressions.Regex.Replace(text, "UltraWinBar|Themes|Aero|GitHub|Windows|Win|Ctrl|IME", "");
            if (System.Text.RegularExpressions.Regex.IsMatch(words, "[A-Za-zÀ-ʯ]")) throw new Exception("Latin Belarusian UI text: " + text);
        }
        Console.WriteLine("PASS: complete Belarusian UI translations and Cyrillic Hebrew month names.");

        // RemoveLanguageDictionaries only inspects each dictionary's Source path, so probe
        // dictionaries can carry a fabricated Source without loading real XAML content.
        var resourceDictionarySourceField = typeof(System.Windows.ResourceDictionary).GetField("_source",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (resourceDictionarySourceField == null) throw new Exception("ResourceDictionary._source field is missing.");
        System.Windows.ResourceDictionary MakeProbeDictionary(string relativePath)
        {
            var dictionary = new System.Windows.ResourceDictionary();
            resourceDictionarySourceField.SetValue(dictionary, new Uri(System.IO.Path.Combine(@"C:\UltraWinBar", relativePath), UriKind.Absolute));
            return dictionary;
        }
        var removeLanguageDictionariesMethod = typeof(DictionaryManager).GetMethod("RemoveLanguageDictionaries",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (removeLanguageDictionariesMethod == null) throw new Exception("DictionaryManager.RemoveLanguageDictionaries is missing.");
        var languageProbeDictionaries = new System.Collections.ObjectModel.Collection<System.Windows.ResourceDictionary>();
        var themeProbeDictionary = MakeProbeDictionary(@"Themes\Core\System.xaml");
        languageProbeDictionaries.Add(themeProbeDictionary);
        // Simulate repeated language switches: each one removes the previous language
        // dictionaries before adding the next, mirroring SetLanguageFromSettings.
        foreach (string languageProbeFile in new[] { @"Languages\English.xaml", @"Languages\Group02\Russian.xaml", @"Languages\Group01\German.xaml" })
        {
            removeLanguageDictionariesMethod.Invoke(null, new object[] { languageProbeDictionaries });
            languageProbeDictionaries.Add(MakeProbeDictionary(languageProbeFile));
            if (languageProbeDictionaries.Count != 2 || languageProbeDictionaries[0] != themeProbeDictionary ||
                languageProbeDictionaries[1].Source.LocalPath != System.IO.Path.Combine(@"C:\UltraWinBar", languageProbeFile))
                throw new Exception("A language switch left stale language dictionaries merged, or disturbed unrelated dictionary order.");
        }
        Console.WriteLine("PASS: repeated language switches remove the previous language dictionaries before adding the next, so exactly one language set stays merged in the same order.");

        var area = new System.Windows.Rect(158, 41, 1604, 998);
        var bar = new ManagedShell.Interop.NativeMethods.Rect { Left = 1762, Top = 41, Right = 1920, Bottom = 1080 };
        var menu = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 0, Right = 509, Bottom = 588 };
        foreach (AppBarEdge edge in Enum.GetValues<AppBarEdge>())
        foreach (bool rtl in new[] { false, true })
        {
            var target = StartMenuPlacement.GetTarget(menu, bar, area, edge, rtl);
            if (!area.Contains(new System.Windows.Rect(target, new System.Windows.Size(menu.Width, menu.Height))))
                throw new Exception($"Menu outside work area: {edge}, RTL={rtl}");
        }
        var rightTarget = StartMenuPlacement.GetTarget(menu, bar, area, AppBarEdge.Right, false);
        if (rightTarget.X != bar.Left - menu.Width) throw new Exception("Right menu width ignored");
        Console.WriteLine("PASS: Start menu bounds for every edge and text direction; full menu size used.");

        // Two stacked bottom panels, pressed on the lower one: the menu sits above both, not over the upper one.
        var stackArea = new System.Windows.Rect(0, 0, 1920, 1080 - 80);
        var lowerBar = new ManagedShell.Interop.NativeMethods.Rect { Left = 0, Top = 1040, Right = 1920, Bottom = 1080 };
        var stackTarget = StartMenuPlacement.GetTarget(menu, lowerBar, stackArea, AppBarEdge.Bottom, false);
        if (stackTarget.Y + menu.Height > stackArea.Bottom || stackTarget.X != 0)
            throw new Exception($"Start menu overlaps a stacked panel: {stackTarget}");
        IntPtr monitorA = (IntPtr)1, monitorB = (IntPtr)2;
        var anchorBars = new List<(IntPtr Monitor, bool HostsStart, bool Primary)>
        {
            (monitorA, false, true), (monitorA, true, true), (monitorB, false, false), (monitorB, true, false)
        };
        if (StartMenuPlacement.ChooseAnchor(anchorBars, monitorA) != 1 || StartMenuPlacement.ChooseAnchor(anchorBars, monitorB) != 3 ||
            StartMenuPlacement.ChooseAnchor(anchorBars.Take(3).ToList(), monitorB) != 2 ||
            StartMenuPlacement.ChooseAnchor(anchorBars.Take(2).ToList(), (IntPtr)3) != 1 ||
            StartMenuPlacement.ChooseAnchor(new List<(IntPtr, bool, bool)>(), monitorA) != -1)
            throw new Exception("Start menu anchor must prefer the Start-hosting panel on the menu's monitor, then any panel there, then the primary Start panel.");
        Console.WriteLine("PASS: a Start menu not opened by our button still anchors to a panel: Start-hosting panel on its monitor first.");

        // Regression: Win+K docked to Explorer's 222 px hidden taskbar, leaving a 64 px gap before our 158 px right panel.
        ManagedShell.Interop.NativeMethods.Rect R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
        var screenRect = R(0, 0, 1920, 1080);
        var connect = R(1338, 41, 1698, 1039);
        var flyoutShift = StartMenuPlacement.GetShellFlyoutShift(connect, R(1698, 0, 1920, 1080), screenRect, area);
        if (flyoutShift != new System.Windows.Vector(64, 0))
            throw new Exception($"Win+K flyout must move flush to the right panel, got {flyoutShift}");
        if (StartMenuPlacement.GetShellFlyoutShift(R(1366, 41, 1762, 1039), R(1698, 0, 1920, 1080), screenRect, area) != null)
            throw new Exception("Action Center already flush with the panel must not move.");
        if (StartMenuPlacement.GetShellFlyoutShift(R(1402, 41, 1762, 1039), R(1762, 0, 1920, 1080), screenRect, area) != null)
            throw new Exception("A flyout already at the area edge must not move.");
        if (StartMenuPlacement.GetShellFlyoutShift(R(200, 200, 600, 600), R(1698, 0, 1920, 1080), screenRect, area) != null)
            throw new Exception("A window not docked to Explorer's taskbar must not move.");
        if (StartMenuPlacement.GetShellFlyoutShift(R(222, 41, 582, 1039), R(0, 0, 222, 1080), screenRect, area) != new System.Windows.Vector(-64, 0))
            throw new Exception("Left-docked flyout must move to the left area edge.");
        if (StartMenuPlacement.GetShellFlyoutShift(R(1500, 500, 1920, 1000), R(0, 1000, 1920, 1080), screenRect, area) != new System.Windows.Vector(0, 39))
            throw new Exception("Bottom-docked flyout must move to the bottom area edge.");
        if (StartMenuPlacement.GetShellFlyoutShift(R(1500, 60, 1920, 400), R(0, 0, 1920, 60), screenRect, area) != new System.Windows.Vector(0, -19))
            throw new Exception("Top-docked flyout must move to the top area edge.");
        Console.WriteLine("PASS: shell flyouts docked to Explorer's hidden taskbar move flush to our panel on every edge; others stay.");

        var startMenuMonitorType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.StartMenuMonitor");
        var shouldHookMenuEvents = startMenuMonitorType?.GetMethod("ShouldHookMenuEvents", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (shouldHookMenuEvents == null) throw new Exception("Start menu event hook policy is missing.");
        bool ShouldHookMenuEvents(bool hasPlacement, bool hasActivatedTaskbar) =>
            (bool)shouldHookMenuEvents.Invoke(null, new object[] { hasPlacement, hasActivatedTaskbar });
        if (ShouldHookMenuEvents(false, false) || !ShouldHookMenuEvents(true, false) ||
            !ShouldHookMenuEvents(false, true) || !ShouldHookMenuEvents(true, true))
            throw new Exception("Menu event hook must stay installed exactly while placement correction or a pending taskbar activation needs it.");
        Console.WriteLine("PASS: Start menu WinEvent hook is required only while placement correction or a pending taskbar activation needs it, not permanently.");
    }
}
