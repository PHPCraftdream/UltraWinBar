using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
        private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
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
        private readonly Tasks tasks;
        private readonly Dispatcher dispatcher;
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
        private ShortcutPreflight preflight;
        private bool preflightPending;
        private bool suppressNextLeftUp;
        private bool disposed;

        private sealed class ShortcutPreflight
        {
            public DesktopShortcutSelection Shortcut { get; init; }
            public ApplicationWindow Window { get; init; }
            public Guid Source { get; init; }
            public Guid Owner { get; init; }
            public long FirstDownAt { get; init; }
            public uint ProcessId { get; init; }
        }

        public DesktopActivationGuard(VirtualDesktopContext desktops, Tasks tasks)
        {
            this.desktops = desktops;
            this.tasks = tasks;
            dispatcher = System.Windows.Application.Current.Dispatcher;
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
            _ = Task.Run(DesktopShortcutResolver.ReadSelectedShortcut);
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

        internal static bool HasUniqueForeignWindow(int currentWindows, int remoteWindows) =>
            currentWindows == 0 && remoteWindows == 1;

        private static uint ProcessIdForWindow(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            return processId;
        }

        private static bool HasSelectionModifiers() =>
            GetAsyncKeyState(0x10) < 0 || GetAsyncKeyState(0x11) < 0 ||
            GetAsyncKeyState(0x12) < 0 || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;

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
                if (!HasUniqueForeignWindow(currentWindows, remote.Length)) return;
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

        private void QueueShortcutPreflight()
        {
            if (preflightPending || tasks == null || HasSelectionModifiers()) return;
            Guid source = desktops.CurrentIdSnapshot();
            if (source == Guid.Empty) return;
            preflightPending = true;
            int generation = intentGeneration;
            long firstDownAt = previousDownAt;
            _ = Task.Run(DesktopShortcutResolver.ReadSelectedShortcut).ContinueWith(result =>
            {
                try
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        preflightPending = false;
                        if (disposed || generation != intentGeneration || !previousDownOnDesktop ||
                            previousDownAt != firstDownAt || desktops.CurrentIdSnapshot() != source ||
                            result.Status != TaskStatus.RanToCompletion || result.Result == null) return;
                        preflight = FindShortcutWindow(result.Result, source, firstDownAt);
                    }), DispatcherPriority.Input);
                }
                catch (Exception) { }
            });
        }

        private ShortcutPreflight FindShortcutWindow(DesktopShortcutSelection shortcut, Guid source, long firstDownAt)
        {
            try
            {
                var windows = tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>()
                    .Where(window => window.CanAddToTaskbar &&
                        string.Equals(window.WinFileName, shortcut.TargetPath, StringComparison.OrdinalIgnoreCase)).ToList();
                int currentWindows = windows.Count(window => desktops.IsOnCurrentDesktop(window.Handle));
                var remote = windows.Where(window =>
                    desktops.TryGetWindowDesktopId(window.Handle, out Guid owner) && owner != source &&
                    !desktops.IsOnCurrentDesktop(window.Handle)).Take(2).ToArray();
                if (!HasUniqueForeignWindow(currentWindows, remote.Length)) return null;
                ApplicationWindow target = remote[0];
                if (!desktops.TryGetWindowDesktopId(target.Handle, out Guid original)) return null;
                ShellLogger.Info($"DesktopActivation: prepared {Path.GetFileName(shortcut.TargetPath)} window={target.Handle} from={original}.");
                return new ShortcutPreflight
                {
                    Shortcut = shortcut, Window = target, Source = source, Owner = original,
                    FirstDownAt = firstDownAt, ProcessId = ProcessIdForWindow(target.Handle)
                };
            }
            catch (Exception error)
            {
                ShellLogger.Warning($"DesktopActivation: shortcut preparation failed: {error.Message}");
                return null;
            }
        }

        private void OpenPreparedShortcut(ShortcutPreflight prepared)
        {
            IntPtr hwnd = prepared.Window.Handle;
            bool moved = false;
            try
            {
                if (!disposed && NativeMethods.IsWindow(hwnd) && desktops.CurrentIdSnapshot() == prepared.Source &&
                    desktops.TryGetWindowDesktopId(hwnd, out Guid owner) && owner == prepared.Owner)
                    moved = desktops.TryMoveWindowToDesktop(hwnd, prepared.Source);
            }
            catch (Exception error)
            {
                ShellLogger.Warning($"DesktopActivation: early shortcut move failed: {error.Message}");
            }

            if (moved)
            {
                armed = false;
                lastMoveDestination = prepared.Source;
                lastMoveOriginal = prepared.Owner;
                lastMovedWindow = hwnd;
                lastMoveAt = Environment.TickCount64;
                try
                {
                    prepared.Window.BringToFront();
                    if (ProcessIdForWindow(GetForegroundWindow()) == ProcessIdForWindow(hwnd))
                    {
                        ShellLogger.Info($"DesktopActivation: opened existing shortcut window={hwnd} without shell launch.");
                        return;
                    }
                }
                catch (Exception error)
                {
                    ShellLogger.Warning($"DesktopActivation: shortcut foreground failed: {error.Message}");
                }
            }
            else if (!disposed)
            {
                intentDesktop = prepared.Source;
                intentProcessId = ProcessIdForWindow(hwnd);
                intentSource = "desktop shortcut fallback";
                intentAt = Environment.TickCount64;
                intentInputTick = lastMouseInputTick;
                armed = true;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = prepared.Shortcut.ShortcutPath, UseShellExecute = true });
                ShellLogger.Info($"DesktopActivation: forwarded shortcut after pre-move={moved}.");
            }
            catch (Exception error)
            {
                CancelIntent();
                ShellLogger.Error($"DesktopActivation: could not open shortcut: {error.Message}");
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
            preflight = null;
            intentGeneration++;
        }

        private void OnMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            if (disposed) return;
            lastMouseInputTick = unchecked((uint)e.HookStruct.time);
            if (suppressNextLeftUp && e.Message == NativeMethods.WM.LBUTTONUP)
            {
                suppressNextLeftUp = false;
                e.Handled = true;
                return;
            }
            if (e.Message != NativeMethods.WM.LBUTTONDOWN)
            {
                if (e.Message == NativeMethods.WM.LBUTTONUP && previousDownOnDesktop &&
                    Environment.TickCount64 - previousDownAt <= GetDoubleClickTime() &&
                    Math.Abs(e.HookStruct.pt.X - previousDownPoint.X) <= GetSystemMetrics(36) &&
                    Math.Abs(e.HookStruct.pt.Y - previousDownPoint.Y) <= GetSystemMetrics(37))
                    QueueShortcutPreflight();
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
                ShortcutPreflight ready = preflight;
                if (ready != null && ready.FirstDownAt == previousDownAt &&
                    ready.Source == desktops.CurrentId && !HasSelectionModifiers() && !dispatcher.HasShutdownStarted &&
                    ready.ProcessId != 0 && ready.ProcessId == ProcessIdForWindow(ready.Window.Handle) &&
                    NativeMethods.IsWindow(ready.Window.Handle))
                {
                    previousDownOnDesktop = false;
                    CancelIntent();
                    intentDesktop = ready.Source;
                    intentAt = now;
                    intentInputTick = lastMouseInputTick;
                    intentSource = "desktop shortcut";
                    try
                    {
                        dispatcher.BeginInvoke(new Action(() => OpenPreparedShortcut(ready)), DispatcherPriority.Send);
                        suppressNextLeftUp = true;
                        e.Handled = true;
                        ShellLogger.Info($"DesktopActivation: intercepted desktop shortcut for window={ready.Window.Handle}.");
                        return;
                    }
                    catch (Exception error)
                    {
                        ShellLogger.Warning($"DesktopActivation: shortcut interception unavailable: {error.Message}");
                    }
                }
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
            preflight = null;
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
