using ManagedShell.Common.Logging;
using ManagedShell.Common.Native;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    internal sealed class DesktopActivationGuard : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(LowLevelMouseHook.POINT point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
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
        // R9-L: shared with panel/element/toolbar drag via InputHookHost instead of a private
        // LowLevelMouseHook + dedicated thread (review Н14, К18): one hook thread for the app.
        private readonly IDisposable mouseSubscription;
        // R9-H: shared with TrayService's EVENT_SYSTEM_FOREGROUND/SkipOwnProcess subscription via
        // the hub instead of a private WinEventHook (review section 5).
        private readonly WinEventHub.Subscription foregroundSubscription;
        private readonly uint ownProcessId = (uint)Process.GetCurrentProcess().Id;
        private LowLevelMouseHook.POINT previousDownPoint;
        private long previousDownAt;
        private volatile bool previousDownOnDesktop;
        private Guid intentDesktop;
        private uint intentProcessId;
        private string intentSource;
        private long intentAt;
        private uint intentInputTick;
        private volatile uint lastMouseInputTick;
        private int intentGeneration;
        private bool armed;
        // Mirrors (armed || lastMoveAt != 0); lets the hook thread skip posting a cancel when nothing is armed.
        private volatile bool hasActiveIntent;
        private Guid lastMoveDestination;
        private Guid lastMoveOriginal;
        private IntPtr lastMovedWindow;
        private long lastMoveAt;
        private volatile ShortcutPreflight preflight;
        private bool preflightPending;
        private bool suppressNextLeftUp;
        private volatile bool disposed;

        private sealed class ShortcutPreflight
        {
            public DesktopShortcutSelection Shortcut { get; init; }
            public ApplicationWindow Window { get; init; }
            public Guid Source { get; init; }
            public Guid Owner { get; init; }
            public long FirstDownAt { get; init; }
            public uint ProcessId { get; init; }
            public bool OpenLocally { get; init; }
        }

        public DesktopActivationGuard(VirtualDesktopContext desktops, Tasks tasks)
        {
            this.desktops = desktops;
            this.tasks = tasks;
            dispatcher = System.Windows.Application.Current.Dispatcher;
            try
            {
                mouseSubscription = InputHookHost.SubscribeMouse("DesktopActivation mouse hook", OnMouseEvent, onHookThread: true);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Desktop activation mouse hook is unavailable.", error);
            }

            foregroundSubscription = WinEventHub.Subscribe("DesktopActivation foreground hook", ForegroundEvent,
                OnForeground, WinEventHook.SkipOwnProcess);
            if (!foregroundSubscription.IsInstalled)
            {
                foregroundSubscription.Dispose();
                mouseSubscription.Dispose();
                throw new InvalidOperationException("Desktop activation foreground hook failed.");
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

        internal static bool HasUniqueForeignWindow(int currentWindows, int remoteWindows) =>
            currentWindows == 0 && remoteWindows == 1;

        internal static bool CompletesDoubleClick(bool previousDownOnDesktop, long previousDownAt, long now,
            int previousX, int previousY, int x, int y, uint doubleClickMs, int xTolerance, int yTolerance) =>
            previousDownOnDesktop && now - previousDownAt <= doubleClickMs &&
            Math.Abs(x - previousX) <= xTolerance && Math.Abs(y - previousY) <= yTolerance;

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
            hasActiveIntent = armed || lastMoveAt != 0;
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
                hasActiveIntent = armed || lastMoveAt != 0;
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
            // previousDownAt is written on the hook thread; read it with Interlocked from this UI-thread callback.
            long firstDownAt = Interlocked.Read(ref previousDownAt);
            _ = Task.Run(DesktopShortcutResolver.ReadSelectedShortcut).ContinueWith(result =>
            {
                try
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        preflightPending = false;
                        if (disposed || generation != intentGeneration || !previousDownOnDesktop ||
                            Interlocked.Read(ref previousDownAt) != firstDownAt || desktops.CurrentIdSnapshot() != source ||
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
                string folderPath = DesktopWindowLauncher.FolderPath(shortcut);
                if (folderPath != null || DesktopWindowLauncher.SupportsNewWindow(shortcut.TargetPath))
                {
                    IntPtr[] folderWindows = folderPath != null ? DesktopShortcutResolver.ReadFolderWindows(folderPath) : null;
                    var currentWindow = tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>()
                        .FirstOrDefault(window => window.CanAddToTaskbar && desktops.IsOnCurrentDesktop(window.Handle) &&
                            (folderWindows != null ? Array.IndexOf(folderWindows, window.Handle) >= 0 :
                                string.Equals(window.WinFileName, shortcut.TargetPath, StringComparison.OrdinalIgnoreCase)));
                    return new ShortcutPreflight
                    {
                        Shortcut = shortcut, Window = currentWindow, Source = source,
                        FirstDownAt = firstDownAt, OpenLocally = true
                    };
                }
                var windows = tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>()
                    .Where(window => window.CanAddToTaskbar &&
                        string.Equals(window.WinFileName, shortcut.TargetPath, StringComparison.OrdinalIgnoreCase)).ToList();
                int currentWindows = windows.Count(window => desktops.IsOnCurrentDesktop(window.Handle));
                if (currentWindows > 0 && DesktopActions.IsSupported)
                {
                    using var actions = new DesktopActions();
                    var shared = windows.FirstOrDefault(window => desktops.IsOnCurrentDesktop(window.Handle) &&
                        actions.IsApplicationPinned(window.Handle));
                    if (shared != null)
                        return new ShortcutPreflight
                        {
                            Shortcut = shortcut, Window = shared, Source = source,
                            FirstDownAt = firstDownAt, OpenLocally = true
                        };
                }
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
            if (prepared.OpenLocally)
            {
                CancelIntent();
                if (disposed || desktops.CurrentIdSnapshot() != prepared.Source) return;
                try
                {
                    if (prepared.Window != null && NativeMethods.IsWindow(prepared.Window.Handle) &&
                        desktops.IsOnCurrentDesktop(prepared.Window.Handle))
                    {
                        prepared.Window.BringToFront();
                        ShellLogger.Info($"DesktopActivation: reused local window={prepared.Window.Handle}.");
                    }
                    else
                    {
                        var start = DesktopWindowLauncher.CreateStartInfo(prepared.Shortcut) ??
                            new ProcessStartInfo { FileName = prepared.Shortcut.ShortcutPath, UseShellExecute = true };
                        DesktopWindowLauncher.Start(start);
                        ShellLogger.Info($"DesktopActivation: new local window; target={start.FileName}.");
                    }
                }
                catch (Exception error)
                {
                    ShellLogger.Error($"DesktopActivation: local launch failed: {error}");
                    System.Windows.MessageBox.Show(error.Message, "UltraWinBar");
                }
                return;
            }
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
                hasActiveIntent = armed || lastMoveAt != 0;
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
                hasActiveIntent = armed || lastMoveAt != 0;
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

        private async void OpenDesktopSelection(Guid source, long clickAt, uint inputTick)
        {
            int generation = intentGeneration;
            hasActiveIntent = true;
            try
            {
                var selections = await Task.Run(DesktopShortcutResolver.ReadSelectedItems);
                if (disposed || generation != intentGeneration || desktops.CurrentIdSnapshot() != source) return;
                if (Environment.TickCount64 - clickAt > IntentDurationMs || !InputStillFromLaunch())
                {
                    CancelIntent();
                    return;
                }
                if (selections.Length == 0) CancelIntent();
                foreach (var selection in selections)
                {
                    var prepared = FindShortcutWindow(selection, source, clickAt);
                    if (prepared != null)
                    {
                        OpenPreparedShortcut(prepared);
                    }
                    else
                    {
                        intentDesktop = source;
                        intentProcessId = 0;
                        intentSource = "desktop icon";
                        intentAt = Environment.TickCount64;
                        intentInputTick = inputTick;
                        armed = true;
                        hasActiveIntent = true;
                        Process.Start(new ProcessStartInfo { FileName = selection.ShortcutPath, UseShellExecute = true });
                    }
                }
            }
            catch (Exception error)
            {
                CancelIntent();
                ShellLogger.Error($"DesktopActivation: desktop launch failed: {error}");
                System.Windows.MessageBox.Show(error.Message, "UltraWinBar");
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
            hasActiveIntent = false;
            preflight = null;
            intentGeneration++;
        }

        // Hook thread: touches only hook-owned fields and the preflight snapshot; intent state changes go through the dispatcher.
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
                if (e.Message == NativeMethods.WM.LBUTTONUP &&
                    CompletesDoubleClick(previousDownOnDesktop, previousDownAt, Environment.TickCount64,
                        previousDownPoint.X, previousDownPoint.Y, e.HookStruct.pt.X, e.HookStruct.pt.Y,
                        GetDoubleClickTime(), GetSystemMetrics(36), GetSystemMetrics(37)))
                    dispatcher.BeginInvoke(new Action(QueueShortcutPreflight), DispatcherPriority.Input);
                if ((uint)e.Message is 0x0204 or 0x0207 or 0x020A or 0x020E)
                {
                    previousDownOnDesktop = false;
                    // Nothing armed: skip the post so wheel/right/middle clicks don't churn the dispatcher.
                    if (hasActiveIntent) dispatcher.BeginInvoke(new Action(CancelIntent), DispatcherPriority.Send);
                }
                return;
            }

            var point = e.HookStruct.pt;
            if (!IsDesktopView(point))
            {
                previousDownOnDesktop = false;
                if (hasActiveIntent) dispatcher.BeginInvoke(new Action(CancelIntent), DispatcherPriority.Send);
                return;
            }

            long now = Environment.TickCount64;
            uint inputTick = lastMouseInputTick;
            if (CompletesDoubleClick(previousDownOnDesktop, previousDownAt, now,
                previousDownPoint.X, previousDownPoint.Y, point.X, point.Y,
                GetDoubleClickTime(), GetSystemMetrics(36), GetSystemMetrics(37)))
            {
                ShortcutPreflight ready = preflight;
                // desktops.CurrentId may be a beat stale here; worst case we skip or intercept a beat late.
                if (!HasSelectionModifiers() && !dispatcher.HasShutdownStarted && desktops.CurrentId != Guid.Empty)
                {
                    Guid source = desktops.CurrentId;
                    long firstDownAt = previousDownAt;
                    previousDownOnDesktop = false;
                    try
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            CancelIntent();
                            intentDesktop = source;
                            intentAt = now;
                            intentInputTick = inputTick;
                            intentSource = "desktop shortcut";
                            if (ready != null && ready.FirstDownAt == firstDownAt &&
                                ready.Source == desktops.CurrentIdSnapshot() &&
                                (ready.OpenLocally || ready.Window != null && NativeMethods.IsWindow(ready.Window.Handle) &&
                                    ready.ProcessId == ProcessIdForWindow(ready.Window.Handle)))
                                OpenPreparedShortcut(ready);
                            else
                                OpenDesktopSelection(intentDesktop, now, inputTick);
                        }), DispatcherPriority.Send);
                        suppressNextLeftUp = true;
                        e.Handled = true;
                        return;
                    }
                    catch (Exception error)
                    {
                        // Dispatcher post itself failed; log off the hook thread instead of doing file I/O here.
                        string message = error.Message;
                        _ = Task.Run(() => ShellLogger.Warning($"DesktopActivation: shortcut interception unavailable: {message}"));
                    }
                }
                previousDownOnDesktop = false;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    CancelIntent();
                    intentDesktop = desktops.CurrentIdSnapshot();
                    intentProcessId = 0;
                    intentSource = "desktop icon with modifiers";
                    intentAt = now;
                    intentInputTick = inputTick;
                    armed = intentDesktop != Guid.Empty;
                    hasActiveIntent = armed || lastMoveAt != 0;
                    if (armed) ShellLogger.Info($"DesktopActivation: desktop double-click on {intentDesktop}.");
                }), DispatcherPriority.Send);
                return;
            }

            preflight = null;
            previousDownPoint = point;
            Interlocked.Exchange(ref previousDownAt, now);
            previousDownOnDesktop = true;
            // hasActiveIntent only gates the post; the inner check stays authoritative against races.
            if (hasActiveIntent) dispatcher.BeginInvoke(new Action(() => { if (armed || lastMoveAt != 0) CancelIntent(); }), DispatcherPriority.Send);
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

        private void OnForeground(uint type, IntPtr hwnd, int obj, int child)
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
                    hasActiveIntent = armed || lastMoveAt != 0;
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
                hasActiveIntent = armed || lastMoveAt != 0;
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
                    hasActiveIntent = armed || lastMoveAt != 0;
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
                hasActiveIntent = armed;
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
            mouseSubscription.Dispose();
            foregroundSubscription.Dispose();
            ShellLogger.Info("DesktopActivation: experimental guard disabled.");
        }
    }
}
