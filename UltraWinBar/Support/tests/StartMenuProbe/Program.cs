using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Spike for hiding the Start menu until it is placed: logs when its window shows, gets focus and moves,
// and with --fade tests whether an out-of-process alpha fade applies to it.
// Usage: dotnet run -c Release [-- --fade]; open Start a few times yourself; exits after 60 s or Ctrl+C.
// Never opens Start itself; with --fade restores the window's original extended style afterwards.
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

    private const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 2, WM_QUIT = 0x12;
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static readonly Dictionary<uint, string> processNames = new();
    private static readonly HashSet<IntPtr> faded = new();
    private static bool fade;

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
        Console.WriteLine($"{clock.ElapsedMilliseconds,7} ms  {name,-10} {hwnd} {cls} [{process}] rect=({r.Left},{r.Top},{r.Right},{r.Bottom}) cloaked={cloaked} lag={Environment.TickCount - (int)time} ms");
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
