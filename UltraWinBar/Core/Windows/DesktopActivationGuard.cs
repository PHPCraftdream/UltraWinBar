using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UltraWinBar.Utilities
{
    internal sealed class DesktopActivationGuard : IDisposable
    {
        private delegate void WinEventProc(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(LowLevelMouseHook.POINT point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        private const uint ForegroundEvent = 3;
        private const long IntentDurationMs = 2000;
        private readonly VirtualDesktopContext desktops;
        private readonly LowLevelMouseHook mouseHook;
        private readonly WinEventProc foregroundCallback;
        private readonly IntPtr foregroundHook;
        private readonly uint ownProcessId = (uint)Process.GetCurrentProcess().Id;
        private LowLevelMouseHook.POINT previousDownPoint;
        private long previousDownAt;
        private bool previousDownOnDesktop;
        private Guid intentDesktop;
        private long intentAt;
        private bool armed;
        private Guid lastMoveDestination;
        private long lastMoveAt;
        private bool disposed;

        public DesktopActivationGuard(VirtualDesktopContext desktops)
        {
            this.desktops = desktops;
            mouseHook = new LowLevelMouseHook();
            mouseHook.LowLevelMouseEvent += OnMouseEvent;
            if (!mouseHook.Initialize())
            {
                mouseHook.LowLevelMouseEvent -= OnMouseEvent;
                mouseHook.Dispose();
                throw new InvalidOperationException("Desktop activation mouse hook is unavailable.");
            }

            foregroundCallback = OnForeground;
            foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, IntPtr.Zero,
                foregroundCallback, 0, 0, 2);
            if (foregroundHook == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                mouseHook.LowLevelMouseEvent -= OnMouseEvent;
                mouseHook.Dispose();
                throw new InvalidOperationException($"Desktop activation foreground hook failed: {error}");
            }

            desktops.Changed += OnDesktopChanged;
            ShellLogger.Info("DesktopActivation: experimental guard enabled.");
        }

        internal static bool ShouldMoveWindow(Guid origin, Guid current, Guid owner, bool onCurrent, long ageMs) =>
            origin != Guid.Empty && origin == current && owner != Guid.Empty && owner != origin &&
            !onCurrent && ageMs >= 0 && ageMs <= IntentDurationMs;

        private void OnMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            if (disposed) return;
            if (e.Message != NativeMethods.WM.LBUTTONDOWN)
            {
                if (e.Message == NativeMethods.WM.RBUTTONDOWN) armed = false;
                return;
            }

            var point = e.HookStruct.pt;
            if (!IsDesktopView(point))
            {
                previousDownOnDesktop = false;
                armed = false;
                return;
            }

            long now = Environment.TickCount64;
            if (previousDownOnDesktop && now - previousDownAt <= GetDoubleClickTime() &&
                Math.Abs(point.X - previousDownPoint.X) <= GetSystemMetrics(36) &&
                Math.Abs(point.Y - previousDownPoint.Y) <= GetSystemMetrics(37))
            {
                previousDownOnDesktop = false;
                intentDesktop = desktops.CurrentIdSnapshot();
                intentAt = now;
                armed = intentDesktop != Guid.Empty;
                if (armed) ShellLogger.Info($"DesktopActivation: desktop double-click on {intentDesktop}.");
                return;
            }

            previousDownOnDesktop = true;
            previousDownPoint = point;
            previousDownAt = now;
        }

        private static bool IsDesktopView(LowLevelMouseHook.POINT point)
        {
            IntPtr hwnd = WindowFromPoint(point);
            bool listView = false;
            for (int depth = 0; depth < 8 && hwnd != IntPtr.Zero; depth++, hwnd = GetParent(hwnd))
            {
                var name = new StringBuilder(64);
                if (GetClassName(hwnd, name, name.Capacity) == 0) return false;
                if (name.ToString() == "SysListView32") listView = true;
                if (name.ToString() == "SHELLDLL_DefView") return listView;
            }
            return false;
        }

        private static bool IsDesktopHost(IntPtr hwnd)
        {
            var name = new StringBuilder(64);
            if (GetClassName(hwnd, name, name.Capacity) == 0) return false;
            return name.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView";
        }

        private void OnForeground(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (disposed || !armed || hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            try
            {
                long age = Environment.TickCount64 - intentAt;
                if (age > IntentDurationMs) { armed = false; return; }
                hwnd = GetAncestor(hwnd, 2);
                if (hwnd == IntPtr.Zero) return;
                if (IsDesktopHost(hwnd)) return;
                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == ownProcessId) return;

                Guid current = desktops.CurrentIdSnapshot();
                if (current != intentDesktop)
                {
                    ShellLogger.Info($"DesktopActivation: desktop switched before foreground; source={intentDesktop}, now={current}.");
                    armed = false;
                    return;
                }
                if (!desktops.TryGetWindowDesktopId(hwnd, out Guid owner))
                {
                    ShellLogger.Info($"DesktopActivation: desktop unknown for window {hwnd}.");
                    armed = false;
                    return;
                }

                bool onCurrent = desktops.IsOnCurrentDesktop(hwnd);
                armed = false;
                if (!ShouldMoveWindow(intentDesktop, current, owner, onCurrent, age))
                {
                    ShellLogger.Info($"DesktopActivation: window={hwnd}, owner={owner}, current={current}, onCurrent={onCurrent}; no move.");
                    return;
                }
                bool moved = desktops.TryMoveWindowToDesktop(hwnd, intentDesktop);
                ShellLogger.Info($"DesktopActivation: window={hwnd}, from={owner}, to={intentDesktop}, moved={moved}.");
                if (moved)
                {
                    lastMoveDestination = intentDesktop;
                    lastMoveAt = Environment.TickCount64;
                }
            }
            catch (Exception error)
            {
                armed = false;
                ShellLogger.Error($"DesktopActivation: foreground handling failed: {error.Message}");
            }
        }

        private void OnDesktopChanged(object sender, EventArgs e)
        {
            if (armed)
            {
                ShellLogger.Info($"DesktopActivation: desktop changed before interception; source={intentDesktop}, now={desktops.CurrentId}.");
                armed = false;
            }
            if (lastMoveAt != 0 && Environment.TickCount64 - lastMoveAt <= IntentDurationMs &&
                desktops.CurrentId != lastMoveDestination)
                ShellLogger.Warning($"DesktopActivation: desktop switched after window move, now={desktops.CurrentId}.");
            lastMoveAt = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            desktops.Changed -= OnDesktopChanged;
            mouseHook.LowLevelMouseEvent -= OnMouseEvent;
            mouseHook.Dispose();
            UnhookWinEvent(foregroundHook);
            ShellLogger.Info("DesktopActivation: experimental guard disabled.");
        }
    }
}
