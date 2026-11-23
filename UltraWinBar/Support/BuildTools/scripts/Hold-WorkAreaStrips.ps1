# Holds the strips between the screen's edges and the current work area as hidden shell AppBars while UltraWinBar is
# stopped and started again by Reinstall-UltraWinBar.ps1: Explorer then recomputes the same work area when the old
# panels die, and maximized windows do not jump. Signals "ready" once the strips are held; lets them go when the new
# instance holds its own (it signals "held"), or after the timeout.
param([int]$TimeoutSeconds = 90)

Add-Type -ReferencedAssemblies System.Windows.Forms @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Threading; using System.Windows.Forms;
public static class StripHolder {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public int uCallbackMessage; public int uEdge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] static extern uint SHAppBarMessage(int msg, ref APPBARDATA data);
  [DllImport("user32.dll")] static extern bool SystemParametersInfo(int a, int b, ref RECT r, int c);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] static extern int RegisterWindowMessage(string name);

  public static void Run(int timeoutSeconds) {
    if (GetSystemMetrics(80) != 1) return;
    var area = new RECT(); SystemParametersInfo(0x30, 0, ref area, 0);
    int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
    var strips = new List<KeyValuePair<int, RECT>>();
    if (area.L > 0) strips.Add(new KeyValuePair<int, RECT>(0, new RECT { L = 0, T = 0, R = area.L, B = h }));
    if (area.T > 0) strips.Add(new KeyValuePair<int, RECT>(1, new RECT { L = 0, T = 0, R = w, B = area.T }));
    if (area.R < w) strips.Add(new KeyValuePair<int, RECT>(2, new RECT { L = area.R, T = 0, R = w, B = h }));
    if (area.B < h) strips.Add(new KeyValuePair<int, RECT>(3, new RECT { L = 0, T = area.B, R = w, B = h }));
    // Not the panels' own callback message: the running instance forwards these to Explorer at once.
    int callback = RegisterWindowMessage("UltraWinBarRestartStrips");
    var windows = new List<NativeWindow>();
    foreach (var strip in strips) {
      var window = new NativeWindow();
      window.CreateHandle(new CreateParams { Style = unchecked((int)0x80000000), ExStyle = 0x80 });
      windows.Add(window);
      var data = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)), hWnd = window.Handle, uCallbackMessage = callback, uEdge = strip.Key, rc = strip.Value };
      SHAppBarMessage(0, ref data);   // ABM_NEW
      SHAppBarMessage(3, ref data);   // ABM_SETPOS
    }
    using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\UltraWinBarRestartStripsReady"))
    using (var held = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\UltraWinBarStripsHeld")) {
      ready.Set();
      var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
      while (DateTime.UtcNow < end && !held.WaitOne(50)) Application.DoEvents();
    }
    foreach (var window in windows) {
      var data = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)), hWnd = window.Handle };
      SHAppBarMessage(1, ref data);   // ABM_REMOVE
      window.DestroyHandle();
    }
  }
}
"@
[StripHolder]::Run($TimeoutSeconds)
