using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.Common.Native;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using ManagedShell.Common.Enums;
using ManagedShell.Common.SupportingClasses;
using static ManagedShell.Interop.NativeMethods;

namespace ManagedShell.WindowsTasks
{
    public class TasksService : DependencyObject, IDisposable
    {
        public static readonly IconSize DEFAULT_ICON_SIZE = IconSize.Small;

        public event EventHandler<WindowEventArgs> WindowActivated;
        public event EventHandler<EventArgs> DesktopActivated;
        public event EventHandler<FullScreenEventArgs> FullScreenChanged;
        public event EventHandler<WindowEventArgs> MonitorChanged;

        private NativeWindowEx _HookWin;
        private object _windowsLock = new object();
        internal bool IsInitialized;
        private IconSize _taskIconSize;

        // R9-N: kept in sync with Windows via CollectionChanged (UI thread, same as every
        // Windows.Add/Remove/Clear call), so shell-message and WinEvent handlers get O(1)
        // handle lookup instead of Windows.Any/First (O(n) each, two passes per lookup).
        private readonly Dictionary<IntPtr, ApplicationWindow> _windowsByHandle = new Dictionary<IntPtr, ApplicationWindow>();

        // K20: typed test access instead of reflection (DesktopRules only, via InternalsVisibleTo).
        internal IReadOnlyDictionary<IntPtr, ApplicationWindow> WindowsByHandle => _windowsByHandle;

        // Н12: cumulative count of dead/reused-handle entries removed by SweepGhosts, exposed for
        // HealthReporter's health line.
        internal static int GhostsRemoved;

        private static int WM_SHELLHOOKMESSAGE = -1;
        private static int WM_TASKBARCREATEDMESSAGE = -1;
        private static int TASKBARBUTTONCREATEDMESSAGE = -1;
        // R9-H: instance-owned WinEventHook (review K12) instead of a raw static SetWinEventHook
        // handle; Dispose always clears its own field, so a following Initialize cannot find a
        // stale non-zero handle and skip re-installing it (was Н3).
        private WinEventHook cloakHook;
        private WinEventHook moveHook;

        // R9-I (K14): explicit state alongside IsInitialized (kept for its existing external
        // reads/writes). Running <-> Stopped is the ordinary Explorer-restart cycle driven by
        // ExplorerMonitor.cs (Dispose then Initialize on every TaskbarCreated).
        internal ServiceLifecycleState LifecycleState { get; private set; } = ServiceLifecycleState.Created;

        internal ITaskCategoryProvider TaskCategoryProvider;
        private TaskCategoryChangeDelegate CategoryChangeDelegate;

        public IconSize TaskIconSize
        {
            get { return _taskIconSize; }
            set
            {
                if (value == _taskIconSize)
                {
                    return;
                }

                _taskIconSize = value;

                if (!IsInitialized)
                {
                    return;
                }

                foreach (var window in Windows)
                {
                    if (!window.ShowInTaskbar)
                    {
                        return;
                    }

                    window.UpdateProperties();
                }
            }
        }

        public TasksService() : this(DEFAULT_ICON_SIZE)
        {
        }
        
        public TasksService(IconSize iconSize)
        {
            // UltraWinBar (Н15): per-instance collection; the DependencyProperty default must
            // never be a shared instance.
            Windows = new ObservableCollection<ApplicationWindow>();
            Windows.CollectionChanged += Windows_CollectionChanged;
            TaskIconSize = iconSize;
        }

        // R9-N: mirrors every Add/Remove/Replace/Reset into _windowsByHandle. Runs synchronously
        // on whatever thread mutated Windows (always the UI thread in practice: WinEvent/shell-hook
        // callbacks and Dispose/Initialize all run there).
        private void Windows_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    foreach (ApplicationWindow win in e.NewItems)
                    {
                        _windowsByHandle[win.Handle] = win;
                        // Н12: freeze the owning PID at add time, if not already captured, so a
                        // later liveness sweep can detect the handle being reused by another process.
                        _ = win.ProcId;
                    }
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    foreach (ApplicationWindow win in e.OldItems)
                    {
                        // Guard against a duplicate-handle entry's removal clobbering the entry
                        // still pointing at the surviving window with the same handle.
                        if (_windowsByHandle.TryGetValue(win.Handle, out var current) && ReferenceEquals(current, win))
                        {
                            _windowsByHandle.Remove(win.Handle);
                            // A remaining duplicate keeps the handle reachable.
                            ApplicationWindow survivor = Windows.FirstOrDefault(w => w.Handle == win.Handle);
                            if (survivor != null) _windowsByHandle[win.Handle] = survivor;
                        }
                    }
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    foreach (ApplicationWindow win in e.OldItems)
                    {
                        if (_windowsByHandle.TryGetValue(win.Handle, out var current) && ReferenceEquals(current, win))
                        {
                            _windowsByHandle.Remove(win.Handle);
                        }
                    }
                    foreach (ApplicationWindow win in e.NewItems)
                    {
                        _windowsByHandle[win.Handle] = win;
                        _ = win.ProcId;
                    }
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    _windowsByHandle.Clear();
                    foreach (ApplicationWindow win in Windows)
                    {
                        _windowsByHandle[win.Handle] = win;
                        _ = win.ProcId;
                    }
                    break;
            }
        }

        internal void Initialize(bool withMultiMonTracking)
        {
            if (IsInitialized)
            {
                return;
            }

            // R9-I (K14): Start after Stop/Dispose is allowed and reinitializes, logged only - not
            // ObjectDisposedException. ExplorerMonitor.cs calls Dispose() then Initialize() on this
            // same instance on every TaskbarCreated; throwing here would permanently disable task
            // tracking after the first Explorer restart. "Disposed" for this service means "stopped,
            // can restart," not "unusable."
            if (LifecycleState == ServiceLifecycleState.Disposed)
            {
                ShellLogger.Info("TasksService: Initialize called after Dispose; reinitializing (Explorer restart path).");
            }

            // UltraWinBar (Н3): track what this attempt actually installed so a mid-init failure
            // can be rolled back instead of leaving a second, half-registered hook window behind.
            bool hookWinCreated = false;
            bool shellHookRegistered = false;
            bool messageReceivedHooked = false;
            bool cloakHookInstalledHere = false;
            bool moveHookInstalledHere = false;

            try
            {
                ShellLogger.Debug("TasksService: Starting");

                // create window to receive task events
                _HookWin = new NativeWindowEx();
                _HookWin.CreateHandle(new CreateParams());
                hookWinCreated = true;

                // prevent other shells from working properly
                SetTaskmanWindow(_HookWin.Handle);

                // register to receive task events
                RegisterShellHookWindow(_HookWin.Handle);
                shellHookRegistered = true;
                WM_SHELLHOOKMESSAGE = RegisterWindowMessage("SHELLHOOK");
                WM_TASKBARCREATEDMESSAGE = RegisterWindowMessage("TaskbarCreated");
                TASKBARBUTTONCREATEDMESSAGE = RegisterWindowMessage("TaskbarButtonCreated");
                _HookWin.MessageReceived += ShellWinProc;
                messageReceivedHooked = true;

                if (EnvironmentHelper.IsWindows8OrBetter)
                {
                    // set event hook for cloak/uncloak events
                    cloakHook = new WinEventHook("TasksService: cloak hook", (uint)EVENT_OBJECT_CLOAKED, (uint)EVENT_OBJECT_UNCLOAKED,
                        CloakEventCallback, WinEventHook.SkipOwnProcess);
                    cloakHookInstalledHere = cloakHook.IsInstalled;
                }

                if (withMultiMonTracking && !EnvironmentHelper.IsWindows8OrBetter)
                {
                    // set event hook for move events
                    // In Windows 8 and newer, use HSHELL_MONITORCHANGED instead
                    moveHook = new WinEventHook("TasksService: move hook", (uint)EVENT_OBJECT_LOCATIONCHANGE, (uint)EVENT_OBJECT_LOCATIONCHANGE,
                        MoveEventCallback, WinEventHook.OutOfContext);
                    moveHookInstalledHere = moveHook.IsInstalled;
                }

                // set window for ITaskbarList
                setTaskbarListHwnd(_HookWin.Handle);

                // adjust minimize animation
                SetMinimizedMetrics();

                // enumerate windows already opened and set active window
                getInitialWindows();

                IsInitialized = true;
                LifecycleState = ServiceLifecycleState.Running;
            }
            catch (Exception ex)
            {
                ShellLogger.Info("TasksService: Unable to start: " + ex.Message);

                // Roll back only what this attempt installed, so a retry does not double-register.
                if (cloakHookInstalledHere)
                {
                    cloakHook?.Dispose();
                    cloakHook = null;
                }

                if (moveHookInstalledHere)
                {
                    moveHook?.Dispose();
                    moveHook = null;
                }

                if (_HookWin != null)
                {
                    if (messageReceivedHooked) _HookWin.MessageReceived -= ShellWinProc;
                    if (shellHookRegistered) DeregisterShellHookWindow(_HookWin.Handle);
                    if (hookWinCreated) _HookWin.DestroyHandle();
                }

                _HookWin = null;
                setTaskbarListHwnd(IntPtr.Zero);
                Windows.Clear();
                LifecycleState = ServiceLifecycleState.Stopped;
            }
        }

        internal void SetTaskCategoryProvider(ITaskCategoryProvider provider)
        {
            TaskCategoryProvider = provider;

            if (CategoryChangeDelegate == null)
            {
                CategoryChangeDelegate = CategoriesChanged;
            }

            TaskCategoryProvider.SetCategoryChangeDelegate(CategoryChangeDelegate);
        }

        private void getInitialWindows()
        {
            // UltraWinBar (Н6): EnumWindows calls back into native code on every window in the
            // session; only collect handles here. Windows.Add (CollectionChanged handlers, task
            // filters, COM calls) runs afterward, off the enumeration callback.
            List<IntPtr> handles = new List<IntPtr>();

            // R9-H: NativeCallback.Wrap reports an escaping exception via CallbackGuard and stops
            // enumeration instead of a local try/catch (review K12).
            EnumWindows(NativeCallback.Wrap("TasksService: getInitialWindows", (hwnd, lParam) =>
            {
                handles.Add(hwnd);
                return true;
            }), IntPtr.Zero);

            foreach (IntPtr surface in handles)
            {
                IntPtr hwnd = WindowGhosting.Original(surface);
                if (_windowsByHandle.ContainsKey(hwnd)) continue;
                ApplicationWindow win = new ApplicationWindow(this, hwnd);

                if (win.CanAddToTaskbar && win.ShowInTaskbar)
                {
                    Windows.Add(win);

                    sendTaskbarButtonCreatedMessage(win.Handle);
                }
            }

            IntPtr hWndForeground = GetForegroundWindow();
            if (Windows.Any(i => i.Handle == hWndForeground && i.ShowInTaskbar))
            {
                ApplicationWindow win = Windows.First(wnd => wnd.Handle == hWndForeground);
                win.State = ApplicationWindow.WindowState.Active;
                win.SetShowInTaskbar();
            }
        }

        public void Dispose()
        {
            if (IsInitialized)
            {
                ShellLogger.Debug("TasksService: Deregistering hooks");
                DeregisterShellHookWindow(_HookWin.Handle);

                // UltraWinBar (Н3): zero the static handles so a following Initialize reinstalls
                // them instead of finding a stale non-zero handle and skipping the hook.
                cloakHook?.Dispose();
                cloakHook = null;

                moveHook?.Dispose();
                moveHook = null;

                _HookWin.MessageReceived -= ShellWinProc;
                _HookWin.DestroyHandle();
                _HookWin = null;
                setTaskbarListHwnd(IntPtr.Zero);
                IsInitialized = false;
                Windows.Clear();
            }

            // R9-I (K14): idempotent regardless of prior state - double Dispose() and Dispose()
            // from Created (never started) both just confirm Stopped.
            LifecycleState = ServiceLifecycleState.Stopped;
            TaskCategoryProvider?.Dispose();
        }

        private void CategoriesChanged()
        {
            foreach (ApplicationWindow window in Windows)
            {
                if (window.ShowInTaskbar)
                {
                    window.Category = TaskCategoryProvider?.GetCategory(window);
                }
            }
        }

        private void SetMinimizedMetrics()
        {
            MinimizedMetrics mm = new MinimizedMetrics
            {
                cbSize = (uint)Marshal.SizeOf(typeof(MinimizedMetrics))
            };

            IntPtr mmPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MinimizedMetrics)));

            try
            {
                Marshal.StructureToPtr(mm, mmPtr, true);
                SystemParametersInfo(SPI.GETMINIMIZEDMETRICS, mm.cbSize, mmPtr, SPIF.None);
                mm.iWidth = 140;
                mm.iArrange |= MinimizedMetricsArrangement.Hide;
                Marshal.StructureToPtr(mm, mmPtr, true);
                SystemParametersInfo(SPI.SETMINIMIZEDMETRICS, mm.cbSize, mmPtr, SPIF.None);
            }
            finally
            {
                Marshal.DestroyStructure(mmPtr, typeof(MinimizedMetrics));
                Marshal.FreeHGlobal(mmPtr);
            }
        }

        public void CloseWindow(ApplicationWindow window)
        {
            if (window.DoClose() != IntPtr.Zero)
            {
                ShellLogger.Debug($"TasksService: Removing window {window.Title} from collection due to no response");
                window.Dispose();
                Windows.Remove(window);
            }
        }

        private void sendTaskbarButtonCreatedMessage(IntPtr hWnd)
        {
            // Server Core doesn't support ITaskbarList, so sending this message on that OS could cause some assuming apps to crash
            if (!EnvironmentHelper.IsServerCore) SendNotifyMessage(hWnd, (uint)TASKBARBUTTONCREATEDMESSAGE, UIntPtr.Zero, IntPtr.Zero);
        }

        private ApplicationWindow addWindow(IntPtr hWnd, ApplicationWindow.WindowState initialState = ApplicationWindow.WindowState.Inactive, bool sanityCheck = false)
        {
            hWnd = WindowGhosting.Original(hWnd);
            if (_windowsByHandle.TryGetValue(hWnd, out var existing)) return existing;
            ApplicationWindow win = new ApplicationWindow(this, hWnd);

            // set window state if a non-default value is provided
            if (initialState != ApplicationWindow.WindowState.Inactive) win.State = initialState;

            // add window unless we need to validate it is eligible to show in taskbar
            if (!sanityCheck || win.CanAddToTaskbar)
            {
                Windows.Add(win);
                ShellLogger.Debug($"TasksService: Added window {hWnd} ({win.Title})");
            }

            // Only send TaskbarButtonCreated if we are shell, and if OS is not Server Core
            // This is because if Explorer is running, it will send the message, so we don't need to
            if (EnvironmentHelper.IsAppRunningAsShell) sendTaskbarButtonCreatedMessage(win.Handle);

            return win;
        }

        private void removeWindow(IntPtr hWnd)
        {
            if (Windows.Any(i => i.Handle == hWnd))
            {
                do
                {
                    ApplicationWindow win = Windows.First(wnd => wnd.Handle == hWnd);
                    win.Dispose();
                    Windows.Remove(win);

                    ShellLogger.Debug($"TasksService: Removed window {hWnd} ({win.Title})");
                }
                while (Windows.Any(i => i.Handle == hWnd));
            }
        }


        internal void ReconcileWindowReplacement(IntPtr hWnd)
        {
            hWnd = WindowGhosting.Original(hWnd);
            if (_windowsByHandle.TryGetValue(hWnd, out var existing))
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                if (!IsWindow(hWnd) || existing.ProcId != processId)
                {
                    removeWindow(hWnd);
                    return;
                }
                existing.UpdateProperties();
                return;
            }
            if (IsWindow(hWnd)) addWindow(hWnd, sanityCheck: true);
        }
        // Н12: rare liveness sweep (called from HealthReporter's existing 30-min timer, not per
        // event). A window is removed from Windows only via a shell-hook message; if that message
        // is ever lost (posted-message queue overflow during a UI-thread stall), the dead entry
        // stays forever, and if the HWND is later reused by a different process, TasksService would
        // silently adopt it under the wrong PID. This finds and removes both cases and counts them.
        internal void SweepGhosts()
        {
            if (_windowsByHandle.Count == 0)
            {
                return;
            }

            List<ApplicationWindow> ghosts = null;
            foreach (KeyValuePair<IntPtr, ApplicationWindow> pair in _windowsByHandle)
            {
                IntPtr hwnd = pair.Key;
                ApplicationWindow win = pair.Value;
                bool isGhost;

                if (!IsWindow(hwnd))
                {
                    isGhost = true;
                }
                else
                {
                    GetWindowThreadProcessId(hwnd, out uint currentPid);
                    uint? recordedPid = win.ProcId;
                    isGhost = recordedPid.HasValue && recordedPid.Value != 0 && currentPid != recordedPid.Value;
                }

                if (isGhost)
                {
                    (ghosts ??= new List<ApplicationWindow>()).Add(win);
                }
            }

            if (ghosts == null)
            {
                return;
            }

            foreach (ApplicationWindow win in ghosts)
            {
                win.Dispose();
                Windows.Remove(win); // CollectionChanged keeps _windowsByHandle in sync
                ShellLogger.Debug($"TasksService: Swept ghost window {win.Handle} ({win.Title})");
            }

            GhostsRemoved += ghosts.Count;
        }

        // R9-N (review section 5): HSHELL_REDRAW (title/icon change) fires per window and, for
        // Explorer, on every folder navigation; it no longer touches same-exe siblings (was O(n)
        // extra UpdateProperties, each possibly doing 1-3 WM_GETICON round-trips, per redraw).
        // HSHELL_FLASH keeps updating siblings via updateSameExeSiblings below.
        private void redrawWindow(ApplicationWindow win)
        {
            win.UpdateProperties();
            ShellLogger.Debug($"TasksService: Updated window {win.Handle} ({win.Title})");
        }

        // Only HSHELL_FLASH calls this: multiple windows of the same exe (e.g. several Explorer
        // windows) are grouped under one taskbar entry, so flashing one should be reflected by the
        // group's other windows too. Kept only for the flash path; REDRAW does not need it.
        private void updateSameExeSiblings(ApplicationWindow win)
        {
            foreach (ApplicationWindow wind in Windows)
            {
                if (wind.WinFileName == win.WinFileName && wind.Handle != win.Handle)
                {
                    wind.UpdateProperties();
                }
            }
        }

        private void ShellWinProc(ref Message msg, ref bool handled)
        {
            Message msgCopy = msg;
            handled = true;
            if (msg.Msg == WM_SHELLHOOKMESSAGE)
            {
                try
                {
                    lock (_windowsLock)
                    {
                        switch ((HSHELL)msg.WParam.ToInt32())
                        {
                            case HSHELL.WINDOWCREATED:
                                if (!_windowsByHandle.TryGetValue(msgCopy.LParam, out ApplicationWindow createdWin))
                                {
                                    addWindow(msg.LParam);
                                }
                                else
                                {
                                    createdWin.UpdateProperties();
                                }
                                break;

                            case HSHELL.WINDOWDESTROYED:
                                IntPtr original = WindowGhosting.Original(msgCopy.LParam);
                                if (original == msgCopy.LParam || !IsWindow(original))
                                    removeWindow(original);
                                break;

                            case HSHELL.WINDOWREPLACING:
                            case HSHELL.WINDOWREPLACED:
                                // Replacement is not destruction: keep the original task identity.
                                ReconcileWindowReplacement(msgCopy.LParam);
                                break;

                            case HSHELL.WINDOWACTIVATED:
                            case HSHELL.RUDEAPPACTIVATED:
                                foreach (var aWin in Windows.Where(w => w.State == ApplicationWindow.WindowState.Active))
                                {
                                    aWin.State = ApplicationWindow.WindowState.Inactive;
                                }

                                if (msg.LParam != IntPtr.Zero)
                                {
                                    ApplicationWindow win = null;

                                    if (_windowsByHandle.TryGetValue(msgCopy.LParam, out ApplicationWindow activatedWin))
                                    {
                                        win = activatedWin;
                                        win.State = ApplicationWindow.WindowState.Active;
                                        win.SetShowInTaskbar();
                                        ShellLogger.Debug($"TasksService: Activated window {win.Handle} ({win.Title})");
                                    }
                                    else
                                    {
                                        win = addWindow(msg.LParam, ApplicationWindow.WindowState.Active);
                                    }

                                    if (win != null)
                                    {
                                        foreach (ApplicationWindow wind in Windows)
                                        {
                                            if (wind.WinFileName == win.WinFileName && wind.Handle != win.Handle)
                                                wind.SetShowInTaskbar();
                                        }

                                        WindowEventArgs args = new WindowEventArgs
                                        {
                                            Window = win
                                        };

                                        WindowActivated?.Invoke(this, args);
                                    }
                                }
                                else
                                {
                                    DesktopActivated?.Invoke(this, new EventArgs());
                                }
                                break;

                            case HSHELL.FLASH:
                                if (_windowsByHandle.TryGetValue(msgCopy.LParam, out ApplicationWindow flashWin))
                                {
                                    if (flashWin.State != ApplicationWindow.WindowState.Active)
                                    {
                                        flashWin.State = ApplicationWindow.WindowState.Flashing;
                                    }

                                    redrawWindow(flashWin);
                                    updateSameExeSiblings(flashWin);
                                }
                                else
                                {
                                    addWindow(msg.LParam, ApplicationWindow.WindowState.Flashing, true);
                                }
                                break;

                            case HSHELL.ACTIVATESHELLWINDOW:
                                ShellLogger.Debug("TasksService: Activate shell window called.");
                                break;

                            case HSHELL.ENDTASK:
                                removeWindow(msg.LParam);
                                break;

                            case HSHELL.REDRAW:
                                if (_windowsByHandle.TryGetValue(msgCopy.LParam, out ApplicationWindow redrawWin))
                                {
                                    if (redrawWin.State == ApplicationWindow.WindowState.Flashing)
                                    {
                                        redrawWin.State = ApplicationWindow.WindowState.Inactive;
                                    }

                                    redrawWindow(redrawWin);
                                }
                                else
                                {
                                    addWindow(msg.LParam, ApplicationWindow.WindowState.Inactive, true);
                                }
                                break;

                            case HSHELL.MONITORCHANGED:
                                if (_windowsByHandle.TryGetValue(msgCopy.LParam, out ApplicationWindow monitorWin))
                                {
                                    monitorWin.SetMonitor();

                                    WindowEventArgs args = new WindowEventArgs
                                    {
                                        Window = monitorWin
                                    };

                                    MonitorChanged?.Invoke(this, args);
                                }
                                break;

                            case HSHELL.FULLSCREENENTER:
                                {
                                    FullScreenEventArgs args = new FullScreenEventArgs
                                    {
                                        Handle = msgCopy.LParam,
                                        IsEntering = true
                                    };

                                    FullScreenChanged?.Invoke(this, args);
                                    ShellLogger.Debug($"TasksService: Full screen entered by window {msgCopy.LParam}");
                                    break;
                                }

                            case HSHELL.FULLSCREENEXIT:
                                {
                                    FullScreenEventArgs args = new FullScreenEventArgs
                                    {
                                        Handle = msgCopy.LParam,
                                        IsEntering = false
                                    };

                                    FullScreenChanged?.Invoke(this, args);
                                    ShellLogger.Debug($"TasksService: Full screen exited by window {msgCopy.LParam}");
                                    break;
                                }

                            case HSHELL.GETMINRECT:
                                // UltraWinBar (Н5/К16): SHELLHOOK is a registered message any
                                // process in the session can post; lParam is not marshaled by the
                                // system. Validate the pointer before reading or writing through it.
                                if (!CrossProcessMessages.TryReadShellHookInfo(msg.LParam, out SHELLHOOKINFO minRectInfo))
                                {
                                    ShellLogger.Error($"TasksService: Rejected GETMINRECT with an invalid pointer ({msg.LParam}).");
                                    break;
                                }

                                if (_windowsByHandle.TryGetValue(minRectInfo.hwnd, out ApplicationWindow minRectWin))
                                {
                                    minRectInfo.rc = minRectWin.GetButtonRectFromShell();

                                    if (minRectInfo.rc.Width <= 0 && minRectInfo.rc.Height <= 0)
                                    {
                                        break;
                                    }
                                    Marshal.StructureToPtr(minRectInfo, msg.LParam, false);
                                    msg.Result = (IntPtr)1;
                                    ShellLogger.Debug($"TasksService: MinRect {minRectInfo.rc.Width}x{minRectInfo.rc.Height} provided for {minRectWin.Handle} ({minRectWin.Title})");
                                    return; // return here so the result isnt reset to DefWindowProc
                                }
                                break;

                            // TaskMan needs to return true if we provide our own task manager to prevent explorers.
                            // case HSHELL.TASKMAN:
                            //     SingletonLogger.Instance.Info("TaskMan Message received.");
                            //     break;

                            default:
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShellLogger.Error("TasksService: Error in ShellWinProc. ", ex);
                }
            }
            else if (msg.Msg == WM_TASKBARCREATEDMESSAGE)
            {
                ShellLogger.Debug("TasksService: TaskbarCreated received, setting ITaskbarList window");
                setTaskbarListHwnd(_HookWin.Handle);
            }
            else if (msg.Msg >= (int)WM.USER)
            {
                // Handle ITaskbarList functions, most not implemented yet

                ApplicationWindow win = null;

                switch (msg.Msg)
                {
                    case (int)WM.USER + 50:
                        // ActivateTab
                        // Also sends WM_SHELLHOOK message
                        ShellLogger.Debug("TasksService: ITaskbarList: ActivateTab HWND:" + msg.LParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 60:
                        // MarkFullscreenWindow
                        // Also sends WM_SHELLHOOK message
                        ShellLogger.Debug("TasksService: ITaskbarList: MarkFullscreenWindow HWND:" + msg.LParam + " Entering? " + msg.WParam);
                        FullScreenEventArgs args = new FullScreenEventArgs
                        {
                            Handle = msgCopy.LParam,
                            IsEntering = msg.WParam != IntPtr.Zero
                        };

                        FullScreenChanged?.Invoke(this, args);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 64:
                        // SetProgressValue
                        ShellLogger.Debug("TasksService: ITaskbarList: SetProgressValue HWND:" + msg.WParam + " Progress: " + msg.LParam);

                        if (_windowsByHandle.TryGetValue(msgCopy.WParam, out win))
                        {
                            win.ProgressValue = (int)msg.LParam;
                        }

                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 65:
                        // SetProgressState
                        ShellLogger.Debug("TasksService: ITaskbarList: SetProgressState HWND:" + msg.WParam + " Flags: " + msg.LParam);

                        if (_windowsByHandle.TryGetValue(msgCopy.WParam, out win))
                        {
                            win.ProgressState = (TBPFLAG)msg.LParam;
                        }

                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 67:
                        // RegisterTab
                        ShellLogger.Debug("TasksService: ITaskbarList: RegisterTab MDI HWND:" + msg.LParam + " Tab HWND: " + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 68:
                        // UnregisterTab
                        ShellLogger.Debug("TasksService: ITaskbarList: UnregisterTab Tab HWND: " + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 71:
                        // SetTabOrder
                        ShellLogger.Debug("TasksService: ITaskbarList: SetTabOrder HWND:" + msg.WParam + " Before HWND: " + msg.LParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 72:
                        // SetTabActive
                        ShellLogger.Debug("TasksService: ITaskbarList: SetTabActive HWND:" + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 75:
                        // Unknown
                        ShellLogger.Debug("TasksService: ITaskbarList: Unknown HWND:" + msg.WParam + " LParam: " + msg.LParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 76:
                        // ThumbBarAddButtons
                        ShellLogger.Debug("TasksService: ITaskbarList: ThumbBarAddButtons HWND:" + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 77:
                        // ThumbBarUpdateButtons
                        ShellLogger.Debug("TasksService: ITaskbarList: ThumbBarUpdateButtons HWND:" + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 78:
                        // ThumbBarSetImageList
                        ShellLogger.Debug("TasksService: ITaskbarList: ThumbBarSetImageList HWND:" + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 79:
                        // SetOverlayIcon - Icon
                        ShellLogger.Debug("TasksService: ITaskbarList: SetOverlayIcon - Icon HWND:" + msg.WParam);

                        if (_windowsByHandle.TryGetValue(msgCopy.WParam, out win))
                        {
                            win.SetOverlayIcon(msg.LParam);
                        }

                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 80:
                        // SetThumbnailTooltip
                        ShellLogger.Debug("TasksService: ITaskbarList: SetThumbnailTooltip HWND:" + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 81:
                        // SetThumbnailClip
                        ShellLogger.Debug("TasksService: ITaskbarList: SetThumbnailClip HWND:" + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 85:
                        // SetOverlayIcon - Description
                        ShellLogger.Debug("TasksService: ITaskbarList: SetOverlayIcon - Description HWND:" + msg.WParam);

                        if (_windowsByHandle.TryGetValue(msgCopy.WParam, out win))
                        {
                            win.SetOverlayIconDescription(msg.LParam);
                        }

                        msg.Result = IntPtr.Zero;
                        return;
                    case (int)WM.USER + 87:
                        // SetTabProperties
                        ShellLogger.Debug("TasksService: ITaskbarList: SetTabProperties HWND:" + msg.WParam);
                        msg.Result = IntPtr.Zero;
                        return;
                    default:
                        ShellLogger.Debug($"TasksService: Unknown ITaskbarList Msg: {msg.Msg} LParam: {msg.LParam} WParam: {msg.WParam}");
                        break;
                }
            }

            handled = false;
        }

        // R9-H: runs inside WinEventHook's own barrier (CallbackGuard), so no local try/catch is
        // needed (was Н6) - an unhandled exception here would otherwise escape into user32 via
        // PropertyChanged -> our filters -> a possible COM call.
        private void MoveEventCallback(uint eventType, IntPtr hWnd, int idObject, int idChild)
        {
            if (hWnd != IntPtr.Zero && idObject == 0 && idChild == 0)
            {
                if (_windowsByHandle.TryGetValue(hWnd, out ApplicationWindow win))
                {
                    win.SetMonitor();
                }
            }
        }

        // R9-H: same trust boundary as MoveEventCallback above.
        private void CloakEventCallback(uint eventType, IntPtr hWnd, int idObject, int idChild)
        {
            if (hWnd != IntPtr.Zero && idObject == 0 && idChild == 0)
            {
                if (_windowsByHandle.TryGetValue(hWnd, out ApplicationWindow win))
                {
                    ShellLogger.Debug($"TasksService: {(eventType == EVENT_OBJECT_CLOAKED ? "Cloak" : "Uncloak")} event received for {win.Title}");
                    win.SetShowInTaskbar();
                }
            }
        }

        // set property on hook window that should receive ITaskbarList messages
        private void setTaskbarListHwnd(IntPtr hwndHook)
        {
            bool resetProp = true;

            // get the topmost tray
            IntPtr taskbarHwnd = WindowHelper.FindWindowsTray(IntPtr.Zero);
            
            if (taskbarHwnd == IntPtr.Zero)
            {
                return;
            }

            // if our tray is running, there may also be a second tray running
            IntPtr systemTaskbarHwnd = WindowHelper.FindWindowsTray(taskbarHwnd);

            if (hwndHook == IntPtr.Zero)
            {
                // no target hwnd provided
                // Try to find and use the handle of the Explorer hook window
                resetProp = false;
                hwndHook = getChildHwndByClass(systemTaskbarHwnd == IntPtr.Zero ? taskbarHwnd : systemTaskbarHwnd, "MSTaskSwWClass");
            }

            if (hwndHook == IntPtr.Zero)
            {
                // if still no hwnd to hook, we can't do anything
                return;
            }

            ShellLogger.Debug("TasksService: Adding TaskbandHWND prop to hwnd: " + taskbarHwnd);
            SetProp(taskbarHwnd, "TaskbandHWND", hwndHook);

            // Remove the property from the Explorer taskbar, if it is not the only tray
            if (resetProp && systemTaskbarHwnd != IntPtr.Zero)
            {
                ShellLogger.Debug("TasksService: Removing TaskbandHWND prop from hwnd: " + systemTaskbarHwnd);
                RemoveProp(systemTaskbarHwnd, "TaskbandHWND");
            }
        }

        private IntPtr getChildHwndByClass(IntPtr parentHwnd, string wndClass)
        {
            IntPtr childHwnd = IntPtr.Zero;
            EnumChildWindows(parentHwnd, (hwnd, lParam) =>
            {
                StringBuilder cName = new StringBuilder(256);
                GetClassName(hwnd, cName, cName.Capacity);
                if (cName.ToString() == wndClass)
                {
                    childHwnd = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return childHwnd;
        }

        internal ObservableCollection<ApplicationWindow> Windows
        {
            get
            {
                return base.GetValue(windowsProperty) as ObservableCollection<ApplicationWindow>;
            }
            set
            {
                SetValue(windowsProperty, value);
            }
        }

        // UltraWinBar (Н15): static registration (a second TasksService would otherwise throw);
        // default value left null so each instance gets its own collection in its constructor.
        private static readonly DependencyProperty windowsProperty = DependencyProperty.Register("Windows",
            typeof(ObservableCollection<ApplicationWindow>), typeof(TasksService),
            new PropertyMetadata(null));
    }
}
