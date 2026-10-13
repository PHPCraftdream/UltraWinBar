using ManagedShell.WindowsTasks;
using System;
using System.Reflection;
using System.Windows.Forms;
using UltraWinBar.Utilities;

// TaskAssignmentManager.GetIdentifier(ExecutablePath)/GetLegacyWindowIdentifier used to build a new
// string on every call; both are now called in nested loops (pin matching, TaskOrderIdentifier's
// per-window ordinal scan, GetAssignedEdge per window x per panel) and cache per ApplicationWindow.
// These checks pin cache-hit identity (same string instance, no rebuild), refresh once a source
// value changes, null/empty handling, and the exact pre-cache string formula.
internal static class IdentifierCacheChecks
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly System.Collections.Generic.List<NativeWindow> created = new();

    private static ApplicationWindow CreateWindow(TasksService service, bool isUwp, string aumid, string winFileName, string className, string title)
    {
        var nativeWindow = new NativeWindow();
        nativeWindow.CreateHandle(new CreateParams());
        created.Add(nativeWindow);
        var window = new ApplicationWindow(service, nativeWindow.Handle);
        var type = typeof(ApplicationWindow);

        // Pre-seed every backing field directly so reading these properties never triggers a real
        // native lookup: _winFileNameRetryAt is pushed into the future so an empty WinFileName stays
        // empty, and ClassName/Title are set (not left null) so their getters skip GetClassName/GetWindowText.
        type.GetField("_isUWP", NonPublicInstance).SetValue(window, (bool?)isUwp);
        type.GetField("_winFileName", NonPublicInstance).SetValue(window, winFileName ?? "");
        type.GetField("_winFileNameRetryAt", NonPublicInstance).SetValue(window, long.MaxValue);
        if (aumid != null) type.GetField("_appUserModelId", NonPublicInstance).SetValue(window, aumid);
        type.GetField("_className", NonPublicInstance).SetValue(window, className ?? "");
        type.GetField("_title", NonPublicInstance).SetValue(window, title ?? "");
        return window;
    }

    private static void SetField(ApplicationWindow window, string field, object value) =>
        typeof(ApplicationWindow).GetField(field, NonPublicInstance).SetValue(window, value);

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        try { RunChecks(); }
        finally
        {
            foreach (var window in created) window.DestroyHandle();
            created.Clear();
        }
    }

    private static void RunChecks()
    {
        var service = new TasksService();

        // Cache hit: same string instance, unchanged sources.
        var stable = CreateWindow(service, false, null, @"C:\apps\app.exe", "Window", "Title");
        string first = TaskAssignmentManager.GetIdentifier(stable, TaskAssignmentMode.ExecutablePath);
        string second = TaskAssignmentManager.GetIdentifier(stable, TaskAssignmentMode.ExecutablePath);
        if (!ReferenceEquals(first, second)) throw new Exception("ExecutablePath identifier cache did not return the same instance on a hit.");
        if (first != @"exe:C:\apps\app.exe") throw new Exception("ExecutablePath identifier formula changed: " + first);

        // Refresh when WinFileName changes.
        SetField(stable, "_winFileName", @"C:\apps\other.exe");
        string third = TaskAssignmentManager.GetIdentifier(stable, TaskAssignmentMode.ExecutablePath);
        if (ReferenceEquals(second, third) || third != @"exe:C:\apps\other.exe")
            throw new Exception("ExecutablePath identifier did not refresh when WinFileName changed.");

        // UWP: AppUserModelID starts empty (a plain window has no shell property store) and arrives later.
        var uwp = CreateWindow(service, true, null, @"C:\Windows\System32\ApplicationFrameHost.exe", "Window", "Title");
        string beforeAumid = TaskAssignmentManager.GetIdentifier(uwp, TaskAssignmentMode.ExecutablePath);
        if (beforeAumid != @"exe:C:\Windows\System32\ApplicationFrameHost.exe")
            throw new Exception("A UWP window with empty AppUserModelID must fall back to WinFileName: " + beforeAumid);
        SetField(uwp, "_appUserModelId", "Contoso.App_8fq!App");
        string afterAumid = TaskAssignmentManager.GetIdentifier(uwp, TaskAssignmentMode.ExecutablePath);
        if (afterAumid != "uwp:Contoso.App_8fq!App" || ReferenceEquals(beforeAumid, afterAumid))
            throw new Exception("ExecutablePath identifier did not refresh once AppUserModelID arrived: " + afterAumid);
        string afterAumidAgain = TaskAssignmentManager.GetIdentifier(uwp, TaskAssignmentMode.ExecutablePath);
        if (!ReferenceEquals(afterAumid, afterAumidAgain))
            throw new Exception("ExecutablePath identifier cache did not return the same instance after a refresh.");

        // Null/empty: no AppUserModelID and no WinFileName -> null, and stays null on repeated calls.
        var unidentifiable = CreateWindow(service, false, null, "", "Window", "Title");
        if (TaskAssignmentManager.GetIdentifier(unidentifiable, TaskAssignmentMode.ExecutablePath) != null ||
            TaskAssignmentManager.GetIdentifier(unidentifiable, TaskAssignmentMode.ExecutablePath) != null)
            throw new Exception("An unidentifiable window's ExecutablePath identifier must stay null.");
        if (TaskAssignmentManager.GetIdentifier(null, TaskAssignmentMode.ExecutablePath) != null)
            throw new Exception("A null window must produce a null ExecutablePath identifier.");

        // Regression table: identifier formula for a range of inputs, matching the pre-cache code.
        (bool isUwp, string aumid, string winFileName, string expected)[] table =
        {
            (true, "app1", null, "uwp:app1"),
            (true, "", @"C:\x.exe", @"exe:C:\x.exe"),
            (false, null, @"C:\y.exe", @"exe:C:\y.exe"),
            (false, null, "", null),
            (true, "", "", null),
        };
        foreach (var row in table)
        {
            var window = CreateWindow(service, row.isUwp, row.aumid, row.winFileName, "Window", "Title");
            string actual = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            if (actual != row.expected)
                throw new Exception($"ExecutablePath identifier mismatch: isUwp={row.isUwp}, aumid={row.aumid}, winFileName={row.winFileName}: expected {row.expected ?? "null"}, got {actual ?? "null"}");
        }
        Console.WriteLine("PASS: ExecutablePath identifier cache hits return the same instance, refresh when WinFileName/AppUserModelID change, handle null/empty windows, and match the exe:/uwp: formula.");

        // GetLegacyWindowIdentifier (WindowClassAndTitle): cache hit is the same instance; a title
        // or class change refreshes it (validated by reference equality, since ApplicationWindow only
        // replaces those backing fields when the value actually changes).
        var legacyWindow = CreateWindow(service, false, null, @"C:\apps\app.exe", "Notepad", "Untitled");
        string legacyFirst = TaskAssignmentManager.GetLegacyWindowIdentifier(legacyWindow);
        string legacySecond = TaskAssignmentManager.GetLegacyWindowIdentifier(legacyWindow);
        if (!ReferenceEquals(legacyFirst, legacySecond)) throw new Exception("Legacy identifier cache did not return the same instance on a hit.");
        if (legacyFirst != "class:Notepad|title:Untitled") throw new Exception("Legacy identifier formula changed: " + legacyFirst);

        SetField(legacyWindow, "_title", "Untitled - Notepad");
        string legacyThird = TaskAssignmentManager.GetLegacyWindowIdentifier(legacyWindow);
        if (ReferenceEquals(legacySecond, legacyThird) || legacyThird != "class:Notepad|title:Untitled - Notepad")
            throw new Exception("Legacy identifier did not refresh when the title changed: " + legacyThird);

        SetField(legacyWindow, "_className", "NotepadWindow");
        string legacyFourth = TaskAssignmentManager.GetLegacyWindowIdentifier(legacyWindow);
        if (ReferenceEquals(legacyThird, legacyFourth) || legacyFourth != "class:NotepadWindow|title:Untitled - Notepad")
            throw new Exception("Legacy identifier did not refresh when the class name changed: " + legacyFourth);

        var emptyLegacy = CreateWindow(service, false, null, "", "", "");
        if (TaskAssignmentManager.GetLegacyWindowIdentifier(emptyLegacy) != null ||
            TaskAssignmentManager.GetLegacyWindowIdentifier(emptyLegacy) != null)
            throw new Exception("A window with no class or title must produce a null legacy identifier.");
        if (TaskAssignmentManager.GetLegacyWindowIdentifier(null) != null)
            throw new Exception("A null window must produce a null legacy identifier.");

        Console.WriteLine("PASS: WindowClassAndTitle legacy identifier cache hits return the same instance and refresh when ClassName or Title changes, matching the class:/title: formula.");

        RunShortPathChecks(service);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathName(string longPath, System.Text.StringBuilder shortPath, uint size);

    private static string ShortPath(string path)
    {
        var sb = new System.Text.StringBuilder(1024);
        return GetShortPathName(path, sb, (uint)sb.Capacity) == 0 ? path : sb.ToString();
    }

    // Regression: a process can report its image as D:\SYSTEM~2\Temp\procexp64.exe while the pin was saved as
    // D:\system_artefact\Temp\procexp64.exe, so the window got its own task instead of taking the pinned button.
    private static void RunShortPathChecks(TasksService service)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBarShortPath" + Guid.NewGuid().ToString("N").Substring(0, 8));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            string longFile = System.IO.Path.Combine(directory, "Sysinternals Process Explorer.exe");
            System.IO.File.WriteAllBytes(longFile, new byte[1]);
            string shortFile = ShortPath(longFile);
            string expected = ExePath.ToLongPath(longFile);
            bool hasAlias = !string.Equals(shortFile, longFile, StringComparison.Ordinal);

            if (ExePath.ToLongPath(@"C:\apps\app.exe") != @"C:\apps\app.exe" || ExePath.CanonicalIdentifier("uwp:Contoso.App!App") != "uwp:Contoso.App!App" ||
                ExePath.CanonicalIdentifier("window:v2:1:2:3") != "window:v2:1:2:3" || ExePath.ToLongPath(null) != null || ExePath.ToLongPath("") != "")
                throw new Exception("Paths without an 8.3 component and non-exe identifiers must come back unchanged.");

            if (hasAlias)
            {
                if (ExePath.ToLongPath(shortFile) != expected || expected.Contains('~'))
                    throw new Exception($"8.3 spelling was not expanded: {ExePath.ToLongPath(shortFile)} vs {expected}");
                if (ExePath.CanonicalIdentifier("exe:" + shortFile) != "exe:" + expected || ExePath.CanonicalIdentifier("pin:exe:" + shortFile) != "pin:exe:" + expected)
                    throw new Exception("exe:/pin:exe: identifiers must expand their path.");
                string missingShort = System.IO.Path.Combine(ShortPath(directory), "gone.exe");
                if (ExePath.ToLongPath(missingShort) != System.IO.Path.Combine(ExePath.ToLongPath(directory), "gone.exe"))
                    throw new Exception("A missing file under a short directory must expand the directory and keep the name.");

                var window = CreateWindow(service, false, null, shortFile, "Window", "Process Explorer");
                string windowId = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
                if (windowId != "exe:" + expected)
                    throw new Exception("A window reporting a short image path must use the long spelling as identifier: " + windowId);
                if (!TaskAssignmentManager.GroupByExecutableIdentifier(new[] { window }).ContainsKey("exe:" + expected))
                    throw new Exception("A pin saved with the long spelling must find the window that reports the short one.");
            }

            // Settings saved with both spellings collapse into one entry per scope, keeping the first position.
            var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
            string longJson = System.Text.Json.JsonSerializer.Serialize("exe:" + expected);
            string shortJson = System.Text.Json.JsonSerializer.Serialize("exe:" + (hasAlias ? shortFile : expected));
            string pinLongJson = System.Text.Json.JsonSerializer.Serialize("pin:exe:" + expected);
            string pinShortJson = System.Text.Json.JsonSerializer.Serialize("pin:exe:" + (hasAlias ? shortFile : expected));
            string desktop = "\"741c8acf-b625-45e8-b44f-5a9942b873ee\"";
            string json = "{\"TaskOrder\":[" +
                "{\"DesktopId\":" + desktop + ",\"Edge\":2,\"Identifier\":" + pinShortJson + "}," +
                "{\"DesktopId\":" + desktop + ",\"Edge\":2,\"Identifier\":\"window:v2:other\"}," +
                "{\"DesktopId\":" + desktop + ",\"Edge\":2,\"Identifier\":" + pinLongJson + "}]," +
                "\"TaskbarAssignments\":[{\"DesktopId\":" + desktop + ",\"Edge\":2,\"Mode\":0,\"Identifier\":" + shortJson + "}," +
                "{\"DesktopId\":" + desktop + ",\"Edge\":2,\"Mode\":0,\"Identifier\":" + longJson + "}]," +
                "\"PinnedApplications\":[{\"DesktopId\":" + desktop + ",\"Edge\":2,\"Identifier\":" + shortJson + "}," +
                "{\"DesktopId\":" + desktop + ",\"Edge\":2,\"Identifier\":" + longJson + "}]}";
            var migrated = System.Text.Json.JsonSerializer.Deserialize(json, settingsType);
            var order = (System.Collections.Generic.List<TaskOrderEntry>)settingsType.GetProperty("TaskOrder").GetValue(migrated);
            var assignments = (System.Collections.Generic.List<TaskbarAssignment>)settingsType.GetProperty("TaskbarAssignments").GetValue(migrated);
            var pins = (System.Collections.Generic.List<PinnedApplication>)settingsType.GetProperty("PinnedApplications").GetValue(migrated);
            bool migrationFlag = (bool)settingsType.GetProperty("MigrationPerformed").GetValue(migrated);
            if (order.Count != 2 || order[0].Identifier != "pin:exe:" + expected || order[1].Identifier != "window:v2:other" ||
                assignments.Count != 1 || assignments[0].Identifier != "exe:" + expected ||
                pins.Count != 1 || pins[0].Identifier != "exe:" + expected)
                throw new Exception($"Saved identifiers were not merged into the long spelling: order={order.Count}, assignments={assignments.Count}, pins={pins.Count}");
            if (hasAlias && !migrationFlag) throw new Exception("Rewriting saved identifiers must request a settings save.");

            // Nothing to rewrite: lists keep their entries and no save is requested.
            var untouched = System.Text.Json.JsonSerializer.Deserialize("{\"TaskOrder\":[{\"Edge\":2,\"Identifier\":\"pin:exe:C:\\\\apps\\\\a.exe\"},{\"Edge\":2,\"Identifier\":\"pin:exe:C:\\\\apps\\\\a.exe\"}]}", settingsType);
            var untouchedOrder = (System.Collections.Generic.List<TaskOrderEntry>)settingsType.GetProperty("TaskOrder").GetValue(untouched);
            if (untouchedOrder.Count != 2 || (bool)settingsType.GetProperty("MigrationPerformed").GetValue(untouched))
                throw new Exception("Settings without 8.3 identifiers must load untouched.");
        }
        finally { System.IO.Directory.Delete(directory, true); }
        Console.WriteLine("PASS: an image path reported in 8.3 form gets the long-spelling identifier, matches the pin saved in long form, and saved short-form pins/assignments/order merge into it.");
    }
}
