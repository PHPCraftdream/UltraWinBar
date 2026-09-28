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
    }
}
