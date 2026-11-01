using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
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

        // An opening line may end with the process behind the window and the processes that started it.
        if (WindowActivityLog.FormatLine(at, "opened", (IntPtr)0x1A2B, @"C:\apps\app.exe", "t", "a.exe(2)[a /x] <- b.exe(1)") !=
            "2026-10-05 07:12:03.045\topened\t0x1A2B\tC:\\apps\\app.exe\tt\ta.exe(2)[a /x] <- b.exe(1)")
            throw new Exception("The origin must be the last column of an opening line.");
        RunProcessOriginChecks();
        Console.WriteLine("PASS: the origin of a window is its process and parents with command lines; gone, reused and cyclic ids end the chain.");

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

            // The origin is written on the opening line only, and a failing lookup never loses the line.
            string originDirectory = directory + "-origin";
            try
            {
                var originSource = new ObservableCollection<ApplicationWindow>();
                using (var journal = new WindowActivityLog(originSource, originDirectory, () => true,
                    handle => handle == nativeWindow.Handle ? "tzutil.exe(30)[tzutil /g] <- cmd.exe(20)" : null))
                {
                    originSource.Add(window);
                    originSource.Remove(window);
                }
                using (var journal = new WindowActivityLog(originSource, originDirectory, () => true,
                    handle => throw new InvalidOperationException("lookup failed")))
                {
                    originSource.Add(window);
                    originSource.Remove(window);
                }
                string[] originLines = File.ReadAllLines(Directory.GetFiles(originDirectory)[0]);
                if (originLines.Length != 4 ||
                    !originLines[0].EndsWith($"\topened\t{hwnd}\tC:\\apps\\editor.exe\tFinal\ttzutil.exe(30)[tzutil /g] <- cmd.exe(20)") ||
                    !originLines[1].EndsWith($"\tclosed\t{hwnd}\tC:\\apps\\editor.exe\tFinal") ||
                    !originLines[2].EndsWith($"\topened\t{hwnd}\tC:\\apps\\editor.exe\tFinal") ||
                    !originLines[3].EndsWith($"\tclosed\t{hwnd}\tC:\\apps\\editor.exe\tFinal"))
                    throw new Exception("The origin belongs on the opening line only, and a failed lookup must still write the line:\n" + string.Join("\n", originLines));
                Console.WriteLine("PASS: the window journal writes the origin on opening lines only and survives a failing lookup.");
            }
            finally
            {
                if (Directory.Exists(originDirectory)) Directory.Delete(originDirectory, true);
            }
        }
        finally
        {
            nativeWindow.DestroyHandle();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void RunProcessOriginChecks()
    {
        ProcessOrigin.Info Process(string name, string command, uint parent, long started) =>
            new ProcessOrigin.Info { Name = name, CommandLine = command, ParentId = parent, StartedAt = started };
        var table = new Dictionary<uint, ProcessOrigin.Info>
        {
            [30] = Process("tzutil.exe", "tzutil /g", 20, 300),
            [20] = Process("cmd.exe", "cmd /c x", 10, 200),
            [10] = Process("explorer.exe", null, 0, 100),
            [40] = Process("a.exe", null, 41, 400),
            [50] = Process("a.exe", null, 51, 500),
            [51] = Process("late.exe", null, 0, 900),
            [60] = Process("a.exe", null, 61, 600),
            [61] = Process("b.exe", null, 60, 500),
            [70] = Process("long.exe", "x\ty\r\n" + new string('z', 300), 0, 700),
        };
        Func<uint, ProcessOrigin.Info> lookup = id => table.TryGetValue(id, out var found) ? found : null;

        if (ProcessOrigin.Describe(30, lookup) != "tzutil.exe(30)[tzutil /g] <- cmd.exe(20)[cmd /c x] <- explorer.exe(10)")
            throw new Exception("The origin must list the process, then each parent, with command lines: " + ProcessOrigin.Describe(30, lookup));
        if (ProcessOrigin.Describe(30, lookup, 2) != "tzutil.exe(30)[tzutil /g] <- cmd.exe(20)[cmd /c x]")
            throw new Exception("The chain must stop after the requested number of levels.");
        if (ProcessOrigin.Describe(99, lookup) != "?(99)")
            throw new Exception("A process that is gone is written as ?(id).");
        if (ProcessOrigin.Describe(40, lookup) != "a.exe(40) <- ?(41)")
            throw new Exception("A parent that is gone ends the chain: " + ProcessOrigin.Describe(40, lookup));
        if (ProcessOrigin.Describe(50, lookup) != "a.exe(50) <- ?(51)")
            throw new Exception("A parent that started after its child is a reused id and must not be named: " + ProcessOrigin.Describe(50, lookup));
        if (ProcessOrigin.Describe(60, lookup) != "a.exe(60) <- b.exe(61)")
            throw new Exception("A cycle of parents must end the chain: " + ProcessOrigin.Describe(60, lookup));
        if (ProcessOrigin.Describe(0, lookup) != "")
            throw new Exception("No process, no origin.");
        string cleaned = ProcessOrigin.Describe(70, lookup);
        if (cleaned.Contains("\t") || cleaned.Contains("\r") || cleaned.Contains("\n") || !cleaned.StartsWith("long.exe(70)[x y  z"))
            throw new Exception("A command line is written on one line: " + cleaned);
        if (cleaned.Length != "long.exe(70)[".Length + 160 + "...".Length + 1 || !cleaned.EndsWith("...]"))
            throw new Exception("A long command line is cut at 160 characters: " + cleaned.Length);

        string own = ProcessOrigin.Describe((uint)Environment.ProcessId, ProcessOrigin.Read);
        if (!own.StartsWith(System.IO.Path.GetFileName(Environment.ProcessPath) + "(" + Environment.ProcessId + ")") || !own.Contains(" <- "))
            throw new Exception("The real lookup must name this process and its parent: " + own);
    }
}
