using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Spike for hiding the Start menu until it is placed: logs when its window shows, gets focus and moves,
// and with --fade tests whether an out-of-process alpha fade applies to it.
// Usage: dotnet run -c Release [-- --fade] [-- --open N [--click X,Y]] [-- --list]; exits after 60 s or Ctrl+C.
// --list: print the panels, the taskbars Open Shell anchors to and ABM_GETTASKBARPOS, then exit.
// --trim PID: empty that process's working set before opening, to reproduce a cold first open.
// Opens Start only with --open N (Win key, or a click at X,Y on our Start button; then Esc; N times); with --fade restores the window's original extended style.
internal static class Program
{
    private delegate void WinEventProc(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr W, L; public uint Time; public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

    private const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 2, WM_QUIT = 0x12;
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static readonly Dictionary<uint, string> processNames = new();
    private static readonly HashSet<IntPtr> faded = new();
    private static bool fade;
    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData { public int Size; public IntPtr Hwnd; public uint Message, Edge; public Rect Rc; public IntPtr Param; }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

    private static int Main(string[] args)
    {
        fade = Array.IndexOf(args, "--fade") >= 0;
        uint thread = GetCurrentThreadId();
        WinEventProc proc = OnEvent;
        var hooks = new[]
        {
            SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, proc, 0, 0, 0),
            SetWinEventHook(0x8002, 0x800B, IntPtr.Zero, proc, 0, 0, 0),
            SetWinEventHook(0x8017, 0x8018, IntPtr.Zero, proc, 0, 0, 0),
        };
        Console.WriteLine($"Probe running ({(fade ? "fade test" : "observe only")}). Open Start a few times; exits in 60 s.");
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (IsWindowVisible(hwnd) && ProcessName(pid) == "UltraWinBar" && GetWindowRect(hwnd, out Rect r))
                Console.WriteLine($"  panel {hwnd} rect=({r.Left},{r.Top},{r.Right},{r.Bottom})");
            return true;
        }, IntPtr.Zero);
        // Taskbars Open Shell may anchor to, with their Start buttons: its menu position derives from them.
        EnumWindows((hwnd, _) =>
        {
            var c = new StringBuilder(256);
            GetClassName(hwnd, c, c.Capacity);
            if (c.ToString() != "Shell_TrayWnd" && c.ToString() != "Shell_SecondaryTrayWnd") return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            GetWindowRect(hwnd, out Rect r);
            Console.WriteLine($"  {c} {hwnd} [{ProcessName(pid)}] visible={IsWindowVisible(hwnd)} rect=({r.Left},{r.Top},{r.Right},{r.Bottom})");
            IntPtr child = IntPtr.Zero;
            while ((child = FindWindowEx(hwnd, child, null, null)) != IntPtr.Zero)
            {
                var cc = new StringBuilder(256);
                GetClassName(child, cc, cc.Capacity);
                GetWindowRect(child, out Rect cr);
                Console.WriteLine($"    child {cc} visible={IsWindowVisible(child)} rect=({cr.Left},{cr.Top},{cr.Right},{cr.Bottom})");
            }
            return true;
        }, IntPtr.Zero);
        var appBar = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        SHAppBarMessage(5, ref appBar); // ABM_GETTASKBARPOS
        Console.WriteLine($"  ABM_GETTASKBARPOS edge={appBar.Edge} rect=({appBar.Rc.Left},{appBar.Rc.Top},{appBar.Rc.Right},{appBar.Rc.Bottom}) hwnd={appBar.Hwnd}");
        if (Array.IndexOf(args, "--list") >= 0) return 0;
        // --trim PID: empty that process's working set first, as Windows does after a long idle (cold first open).
        int t = Array.IndexOf(args, "--trim");
        if (t >= 0 && t + 1 < args.Length)
        {
            using var target = Process.GetProcessById(int.Parse(args[t + 1]));
            Console.WriteLine($"  trimmed {target.ProcessName} {target.Id}: {EmptyWorkingSet(target.Handle)} (working set was {target.WorkingSet64 / 1048576} MB)");
        }
        int open = Array.IndexOf(args, "--open") is int i && i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : 0;
        int c = Array.IndexOf(args, "--click");
        int[] click = c >= 0 && c + 1 < args.Length ? Array.ConvertAll(args[c + 1].Split(','), int.Parse) : null;
        if (open > 0)
        {
            new Thread(() =>
            {
                for (int n = 0; n < open; n++)
                {
                    Thread.Sleep(1500);
                    Console.WriteLine($"{clock.ElapsedMilliseconds,7} ms  -- {(click == null ? "Win key" : "click")} #{n + 1}");
                    if (click == null) { keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero); }
                    else { SetCursorPos(click[0], click[1]); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
                    Thread.Sleep(1500);
                    keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero);
                }
                Thread.Sleep(1000);
                PostThreadMessage(thread, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }) { IsBackground = true }.Start();
        }
        using var stop = new Timer(_ => PostThreadMessage(thread, WM_QUIT, IntPtr.Zero, IntPtr.Zero), null, 60000, Timeout.Infinite);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; PostThreadMessage(thread, WM_QUIT, IntPtr.Zero, IntPtr.Zero); };
        while (GetMessage(out Msg msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        foreach (var hook in hooks) UnhookWinEvent(hook);
        GC.KeepAlive(proc);
        return 0;
    }

    private static string ProcessName(uint pid)
    {
        if (processNames.TryGetValue(pid, out string name)) return name;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { name = "?"; }
        return processNames[pid] = name;
    }

    private static void OnEvent(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        string process = ProcessName(pid);
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        bool start = process == "StartMenuExperienceHost" || cls.ToString().StartsWith("OpenShell.", StringComparison.Ordinal) ||
            cls.ToString() == "DV2ControlHost";
        if (!start) return;
        string name = type switch
        {
            0x0003 => "FOREGROUND", 0x8002 => "SHOW", 0x8003 => "HIDE", 0x8004 => "REORDER", 0x800B => "LOCATION",
            0x8017 => "CLOAKED", 0x8018 => "UNCLOAKED", _ => $"0x{type:X4}"
        };
        if (name.StartsWith("0x", StringComparison.Ordinal) || type == 0x8004) return;
        GetWindowRect(hwnd, out Rect r);
        DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int));
        // Alpha only for LWA_ALPHA layering (UltraWinBar hides the menu this way until it is placed).
        string alpha = (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(hwnd, out _, out byte a, out uint f) && (f & LWA_ALPHA) != 0
            ? $" alpha={a}" : "";
        Console.WriteLine($"{clock.ElapsedMilliseconds,7} ms  {name,-10} {hwnd} {cls} [{process}] rect=({r.Left},{r.Top},{r.Right},{r.Bottom}) cloaked={cloaked}{alpha} lag={Environment.TickCount - (int)time} ms");
        if (fade && (type == 0x8002 || type == 0x8018) && faded.Add(hwnd)) TryFade(hwnd);
        if (type == 0x8003 || type == 0x8017) faded.Remove(hwnd);
    }

    private static void TryFade(IntPtr hwnd)
    {
        int original = GetWindowLong(hwnd, GWL_EXSTYLE);
        int set = SetWindowLong(hwnd, GWL_EXSTYLE, original | WS_EX_LAYERED);
        int setError = Marshal.GetLastWin32Error();
        bool hidden = SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
        int hideError = Marshal.GetLastWin32Error();
        GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags);
        Console.WriteLine($"          fade: exstyle 0x{original:X8} -> set={set != 0} err={setError}; alpha0={hidden} err={hideError}; now alpha={alpha} flags={flags}");
        int cloak = 1;
        int cloakResult = DwmSetWindowAttribute(hwnd, 13, ref cloak, sizeof(int));
        cloak = 0;
        if (cloakResult == 0) DwmSetWindowAttribute(hwnd, 13, ref cloak, sizeof(int));
        Console.WriteLine($"          DWMWA_CLOAK from another process: hr=0x{cloakResult:X8}");
        new Thread(() =>
        {
            Thread.Sleep(300);
            for (int step = 1; step <= 12; step++)
            {
                SetLayeredWindowAttributes(hwnd, 0, (byte)(step * 255 / 12), LWA_ALPHA);
                Thread.Sleep(16);
            }
            SetWindowLong(hwnd, GWL_EXSTYLE, original);
            Console.WriteLine($"          fade done at {clock.ElapsedMilliseconds} ms, exstyle restored");
        }) { IsBackground = true }.Start();
    }
}
