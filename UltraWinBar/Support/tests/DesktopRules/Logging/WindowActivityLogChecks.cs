using ManagedShell.WindowsTasks;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using UltraWinBar.Utilities;

// Opt-in window open/close journal: one line per event (time, kind, hwnd, binary, title),
// written only while the flag is on, the closing line keeps the last known binary/title.
internal static class WindowActivityLogChecks
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static void Run()
    {
        var at = new DateTime(2026, 10, 5, 7, 12, 3, 45);
        string line = WindowActivityLog.FormatLine(at, "opened", (IntPtr)0x1A2B, @"C:\apps\app.exe", "Tab\there\r\nnext");
        if (line != "2026-10-05 07:12:03.045\topened\t0x1A2B\tC:\\apps\\app.exe\tTab here  next")
            throw new Exception("Window activity line format changed: " + line);
        if (WindowActivityLog.FormatLine(at, "closed", (IntPtr)1, null, "") != "2026-10-05 07:12:03.045\tclosed\t0x1\t-\t-")
            throw new Exception("Missing binary/title must be written as '-'.");
        if (WindowActivityLog.FileNameFor(at) != "windows-2026-10-05.log")
            throw new Exception("Window activity file name changed.");
        Console.WriteLine("PASS: window activity lines are tab-separated, single-line, and dated per file.");

        // The settings checkbox alone enables the journal, independently of debug logging.
        bool previousSetting = Settings.Instance.WindowActivityLogging, previousDebug = Settings.Instance.DebugLogging;
        try
        {
            Settings.Instance.DebugLogging = false;
            Settings.Instance.WindowActivityLogging = true;
            if (!WindowActivityLog.IsEnabled()) throw new Exception("The WindowActivityLogging setting must enable the window journal.");
            Settings.Instance.DebugLogging = true;
            Settings.Instance.WindowActivityLogging = false;
            if (WindowActivityLog.IsEnabled() != File.Exists(WindowActivityLog.FlagFileName.InLocalAppData()))
                throw new Exception("Debug logging must not enable the window journal.");
        }
        finally
        {
            Settings.Instance.WindowActivityLogging = previousSetting;
            Settings.Instance.DebugLogging = previousDebug;
        }
        Console.WriteLine("PASS: the window journal has its own setting, separate from debug logging.");

        string directory = Path.Combine(Path.GetTempPath(), "UltraWinBar-window-log-" + Guid.NewGuid().ToString("N"));
        var nativeWindow = new NativeWindow();
        nativeWindow.CreateHandle(new CreateParams());
        try
        {
            var window = new ApplicationWindow(new TasksService(), nativeWindow.Handle);
            typeof(ApplicationWindow).GetField("_winFileName", NonPublicInstance).SetValue(window, @"C:\apps\editor.exe");
            typeof(ApplicationWindow).GetField("_title", NonPublicInstance).SetValue(window, "Draft");
            var source = new ObservableCollection<ApplicationWindow>();
            bool enabled = false;
            using (new WindowActivityLog(source, directory, () => enabled))
            {
                source.Add(window);
                if (Directory.Exists(directory) && Directory.GetFiles(directory).Length > 0)
                    throw new Exception("Window activity must not be written while the flag is off.");
                source.Remove(window);
                enabled = true;
                source.Add(window);
                typeof(ApplicationWindow).GetField("_title", NonPublicInstance).SetValue(window, "Final");
                source.Remove(window);
            }
            source.Add(window);
            string[] files = Directory.GetFiles(directory);
            if (files.Length != 1) throw new Exception($"Expected one window activity file, found {files.Length}.");
            string[] lines = File.ReadAllLines(files[0]);
            string hwnd = "0x" + nativeWindow.Handle.ToInt64().ToString("X");
            if (lines.Length != 2 ||
                !lines[0].EndsWith($"\topened\t{hwnd}\tC:\\apps\\editor.exe\tDraft") ||
                !lines[1].EndsWith($"\tclosed\t{hwnd}\tC:\\apps\\editor.exe\tFinal"))
                throw new Exception("Window activity must log open and close (last title) only while enabled and only until disposed:\n" + string.Join("\n", lines));
            Console.WriteLine("PASS: window activity logs opened/closed with binary and last title only while enabled.");
        }
        finally
        {
            nativeWindow.DestroyHandle();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
