using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

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
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint Size;
            public uint Tick;
        }

        private const uint ForegroundEvent = 3;
        private const long IntentDurationMs = 5000;
        private const long ReturnWindowMs = 6000;
        private readonly VirtualDesktopContext desktops;
        private readonly LowLevelMouseHook mouseHook;
        private readonly WinEventProc foregroundCallback;
        private readonly IntPtr foregroundHook;
        private readonly uint ownProcessId = (uint)Process.GetCurrentProcess().Id;
        private LowLevelMouseHook.POINT previousDownPoint;
        private long previousDownAt;
        private bool previousDownOnDesktop;
        private Guid intentDesktop;
        private uint intentProcessId;
        private string intentSource;
        private long intentAt;
        private uint intentInputTick;
        private uint lastMouseInputTick;
        private int intentGeneration;
        private bool armed;
        private Guid lastMoveDestination;
        private Guid lastMoveOriginal;
        private IntPtr lastMovedWindow;
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

        internal static bool ShouldRecoverAfterSwitch(Guid origin, Guid current, Guid owner, long ageMs, bool inputIntact) =>
            inputIntact && origin != Guid.Empty && current != Guid.Empty && current != origin &&
            owner == current && ageMs >= 0 && ageMs <= IntentDurationMs;

        internal static bool ShouldReturnAfterMove(Guid origin, Guid original, Guid current, Guid owner,
            long ageMs, bool inputIntact) =>
            inputIntact && origin != Guid.Empty && original != Guid.Empty && current == original &&
            current != origin && owner == origin && ageMs >= 0 && ageMs <= ReturnWindowMs;

        internal static bool ShouldPreMoveTrayWindow(bool doubleClick, int currentWindows, int remoteWindows) =>
            doubleClick && currentWindows == 0 && remoteWindows == 1;

        private static uint ProcessIdForWindow(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            return processId;
        }

        internal void ArmTrayIntent(IntPtr ownerHwnd, Tasks tasks, bool preMove)
        {
            if (disposed || ownerHwnd == IntPtr.Zero) return;
            uint processId = ProcessIdForWindow(ownerHwnd);
            uint shellProcessId = ProcessIdForWindow(GetShellWindow());
            if (processId == 0 || processId == ownProcessId || processId == shellProcessId) return;

            Guid source = desktops.CurrentIdSnapshot();
            if (source == Guid.Empty) return;
            CancelIntent();
            intentDesktop = source;
            intentProcessId = processId;
            intentSource = "tray";
            intentAt = Environment.TickCount64;
            intentInputTick = lastMouseInputTick;
            armed = true;
            ShellLogger.Info($"DesktopActivation: tray click, process={processId}, source={source}, doubleClick={preMove}.");
            if (!preMove || tasks == null) return;

            try
            {
                var windows = tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>()
                    .Where(window => window.CanAddToTaskbar && ProcessIdForWindow(window.Handle) == processId).ToList();
                int currentWindows = windows.Count(window => desktops.IsOnCurrentDesktop(window.Handle));
                var remote = windows.Where(window =>
                    desktops.TryGetWindowDesktopId(window.Handle, out Guid owner) && owner != source &&
                    !desktops.IsOnCurrentDesktop(window.Handle)).Take(2).ToArray();
                if (!ShouldPreMoveTrayWindow(preMove, currentWindows, remote.Length)) return;
                IntPtr hwnd = remote[0].Handle;
                if (!desktops.TryGetWindowDesktopId(hwnd, out Guid original)) return;
                bool moved = desktops.TryMoveWindowToDesktop(hwnd, source);
                ShellLogger.Info($"DesktopActivation: tray pre-move window={hwnd}, from={original}, to={source}, moved={moved}.");
                if (!moved) return;
                armed = false;
                lastMoveDestination = source;
                lastMoveOriginal = original;
                lastMovedWindow = hwnd;
                lastMoveAt = Environment.TickCount64;
            }
            catch (Exception error)
            {
                ShellLogger.Warning($"DesktopActivation: tray pre-move unavailable: {error.Message}");
            }
        }

        private bool InputStillFromLaunch()
        {
            var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref input)) return false;
            int mouseDelta = unchecked((int)(input.Tick - lastMouseInputTick));
            int clickDelta = unchecked((int)(input.Tick - intentInputTick));
            return mouseDelta >= -100 && mouseDelta <= 100 || clickDelta >= -100 && clickDelta <= 350;
        }

        private void CancelIntent()
        {
            armed = false;
            intentProcessId = 0;
            lastMoveAt = 0;
            intentGeneration++;
        }

        private void OnMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            if (disposed) return;
            lastMouseInputTick = unchecked((uint)e.HookStruct.time);
            if (e.Message != NativeMethods.WM.LBUTTONDOWN)
            {
                if ((uint)e.Message is 0x0204 or 0x0207 or 0x020A or 0x020E)
                {
                    previousDownOnDesktop = false;
                    CancelIntent();
                }
                return;
            }

            var point = e.HookStruct.pt;
            if (!IsDesktopView(point))
            {
                previousDownOnDesktop = false;
                CancelIntent();
                return;
            }

            long now = Environment.TickCount64;
            if (previousDownOnDesktop && now - previousDownAt <= GetDoubleClickTime() &&
                Math.Abs(point.X - previousDownPoint.X) <= GetSystemMetrics(36) &&
                Math.Abs(point.Y - previousDownPoint.Y) <= GetSystemMetrics(37))
            {
                previousDownOnDesktop = false;
                CancelIntent();
                intentDesktop = desktops.CurrentIdSnapshot();
                intentProcessId = 0;
                intentSource = "desktop icon";
                intentAt = now;
                intentInputTick = lastMouseInputTick;
                armed = intentDesktop != Guid.Empty;
                if (armed) ShellLogger.Info($"DesktopActivation: desktop double-click on {intentDesktop}.");
                return;
            }

            if (armed || lastMoveAt != 0) CancelIntent();
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
                if (age < 0 || age > IntentDurationMs)
                {
                    ShellLogger.Info($"DesktopActivation: intent expired after {age} ms.");
                    CancelIntent();
                    return;
                }
                hwnd = GetAncestor(hwnd, 2);
                if (hwnd == IntPtr.Zero) return;
                if (IsDesktopHost(hwnd)) return;
                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == ownProcessId) return;
                if (intentProcessId != 0 && processId != intentProcessId) return;
                if (!InputStillFromLaunch())
                {
                    ShellLogger.Info("DesktopActivation: newer user input cancelled the launch intent.");
                    CancelIntent();
                    return;
                }

                Guid current = desktops.CurrentIdSnapshot();
                if (!desktops.TryGetWindowDesktopId(hwnd, out Guid owner))
                {
                    ShellLogger.Info($"DesktopActivation: desktop unknown for window {hwnd}.");
                    CancelIntent();
                    return;
                }

                if (current != intentDesktop)
                {
                    bool recover = ShouldRecoverAfterSwitch(intentDesktop, current, owner, age, true);
                    armed = false;
                    if (!recover)
                    {
                        ShellLogger.Info($"DesktopActivation: switched before foreground; window={hwnd}, owner={owner}, now={current}; no move.");
                        CancelIntent();
                        return;
                    }
                    bool recovered = desktops.TryMoveWindowToDesktop(hwnd, intentDesktop);
                    ShellLogger.Info($"DesktopActivation: {intentSource} switched before foreground; window={hwnd}, from={owner}, to={intentDesktop}, moved={recovered}.");
                    if (recovered) QueueReturn(intentDesktop, current, hwnd, "early switch");
                    else CancelIntent();
                    return;
                }

                bool onCurrent = desktops.IsOnCurrentDesktop(hwnd);
                armed = false;
                if (!ShouldMoveWindow(intentDesktop, current, owner, onCurrent, age))
                {
                    ShellLogger.Info($"DesktopActivation: window={hwnd}, owner={owner}, current={current}, onCurrent={onCurrent}; no move.");
                    CancelIntent();
                    return;
                }
                bool moved = desktops.TryMoveWindowToDesktop(hwnd, intentDesktop);
                ShellLogger.Info($"DesktopActivation: {intentSource} window={hwnd}, from={owner}, to={intentDesktop}, moved={moved}.");
                if (moved)
                {
                    lastMoveDestination = intentDesktop;
                    lastMoveOriginal = owner;
                    lastMovedWindow = hwnd;
                    lastMoveAt = Environment.TickCount64;
                }
                else CancelIntent();
            }
            catch (Exception error)
            {
                CancelIntent();
                ShellLogger.Error($"DesktopActivation: foreground handling failed: {error.Message}");
            }
        }

        private void OnDesktopChanged(object sender, EventArgs e)
        {
            try
            {
                long now = Environment.TickCount64;
                if (armed)
                {
                    if (now - intentAt > IntentDurationMs || !InputStillFromLaunch()) CancelIntent();
                    else ShellLogger.Info($"DesktopActivation: {intentSource} changed before foreground; source={intentDesktop}, now={desktops.CurrentId}.");
                }
                if (lastMoveAt == 0) return;
                bool knownWindow = desktops.TryGetWindowDesktopId(lastMovedWindow, out Guid owner);
                bool inputIntact = InputStillFromLaunch();
                long age = now - lastMoveAt;
                bool restore = knownWindow && ShouldReturnAfterMove(lastMoveDestination, lastMoveOriginal,
                    desktops.CurrentId, owner, age, inputIntact);
                lastMoveAt = 0;
                if (!restore)
                {
                    ShellLogger.Info($"DesktopActivation: return skipped; now={desktops.CurrentId}, owner={owner}, age={age}, inputIntact={inputIntact}.");
                    return;
                }
                ShellLogger.Warning($"DesktopActivation: delayed switch to {desktops.CurrentId}; returning to {lastMoveDestination}.");
                QueueReturn(lastMoveDestination, desktops.CurrentId, lastMovedWindow, "delayed switch");
            }
            catch (Exception error)
            {
                CancelIntent();
                ShellLogger.Error($"DesktopActivation: desktop-change handling failed: {error.Message}");
            }
        }

        private void QueueReturn(Guid origin, Guid unexpected, IntPtr hwnd, string reason)
        {
            if (!DesktopActions.IsSupported)
            {
                ShellLogger.Warning("DesktopActivation: return to the original desktop is unavailable on this Windows build.");
                return;
            }
            int generation = intentGeneration;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            dispatcher?.BeginInvoke(new Action(() =>
            {
                if (disposed || generation != intentGeneration) return;
                if (!InputStillFromLaunch())
                {
                    ShellLogger.Info("DesktopActivation: return cancelled by newer input.");
                    return;
                }
                Guid current = desktops.CurrentIdSnapshot();
                Guid owner = Guid.Empty;
                if (current != unexpected || !desktops.TryGetWindowDesktopId(hwnd, out owner) || owner != origin)
                {
                    ShellLogger.Info($"DesktopActivation: return cancelled; current={current}, window={hwnd}, owner={owner}.");
                    return;
                }
                try
                {
                    using var actions = new DesktopActions();
                    actions.SwitchDesktop(origin);
                    ShellLogger.Info($"DesktopActivation: returned to {origin} after {reason}; window={hwnd}.");
                }
                catch (Exception error)
                {
                    ShellLogger.Warning($"DesktopActivation: could not return to {origin}: {error.Message}");
                }
            }), DispatcherPriority.Send);
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
