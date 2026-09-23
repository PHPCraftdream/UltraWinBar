using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Common.SupportingClasses;
using ManagedShell.UWPInterop;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    public class StartMenuMonitor : IDisposable
    {
        private AppVisibilityHelper _appVisibilityHelper;
        private DispatcherTimer _poller;
        private Action _correctPlacement;
        private IntPtr _positionedMenu;
        private IntPtr _menuEventHook;
        private WinEventDelegate _menuEventProc;
        private bool _correctingPlacement;
        private bool _isVisible;
        private IntPtr _taskbarHwndActivated;

        // Fast path for the modern Start menu: EVENT_SYSTEM_FOREGROUND fires the instant it
        // becomes the foreground window, instead of waiting for the next 100ms poller tick
        // (during which the window is already visibly painted at the OS's default position).
        // Kept as a supplement, not a replacement — the poller still drives Open Shell/classic
        // detection and close detection, where a 100ms delay isn't visually noticeable.
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        private IntPtr _foregroundEventHook;
        private WinEventDelegate _foregroundEventProc;

        public event EventHandler<StartMenuMonitorEventArgs> StartMenuVisibilityChanged;

        public StartMenuMonitor(AppVisibilityHelper appVisibilityHelper)
        {
            _appVisibilityHelper = appVisibilityHelper;
            setupPoller();
            setupForegroundHook();
            _menuEventProc = MenuEventProc;
            _menuEventHook = SetWinEventHook(0x8001, 0x800B, IntPtr.Zero, _menuEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        private void MenuEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0 || _correctingPlacement) return;
            if (eventType == 0x8001 || eventType == 0x8003)
            {
                if (hwnd == _positionedMenu)
                {
                    _positionedMenu = IntPtr.Zero;
                    _correctPlacement = null;
                }
                return;
            }
            if (eventType != 0x8002 && eventType != 0x800B) return;
            if (_correctPlacement == null && _taskbarHwndActivated == IntPtr.Zero) return;
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "OpenShell.CMenuContainer" && _positionedMenu == IntPtr.Zero)
                relocateStartMenu(hwnd);
            if (hwnd == _positionedMenu || name.ToString() == "OpenShell.CUserWindow")
                CorrectPlacement();
        }

        private void CorrectPlacement()
        {
            if (_correctingPlacement) return;
            _correctingPlacement = true;
            try { _correctPlacement?.Invoke(); }
            finally { _correctingPlacement = false; }
        }

        private void setupForegroundHook()
        {
            _foregroundEventProc = ForegroundEventProc;
            _foregroundEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        private void ForegroundEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || !EnvironmentHelper.IsWindows8OrBetter)
            {
                return;
            }

            StringBuilder cName = new StringBuilder(256);
            GetClassName(hwnd, cName, cName.Capacity);
            string className = cName.ToString();
            ShellLogger.Debug($"StartMenuMonitor DIAG: foreground changed, hwnd={hwnd}, class={className}");

            // Modern Start (Win10/11) and Open Shell Menu both become foreground when they
            // open; classic Start (DV2ControlHost) does not reliably, so it stays on the
            // poller below.
            if (className != "Windows.UI.Core.CoreWindow" && className != "OpenShell.CMenuContainer")
            {
                return;
            }

            ShellLogger.Debug($"StartMenuMonitor DIAG: foreground hook matched {className}, relocating");
            relocateStartMenu(hwnd);
            setVisibility(true, MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));
        }

        private void setupPoller()
        {
            if (_poller == null)
            {
                _poller = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };

                _poller.Tick += poller_Tick;
            }

            _poller.Start();
        }

        private void poller_Tick(object sender, EventArgs e)
        {
            bool newIsVisible = false;
            IntPtr startHmonitor = IntPtr.Zero;

            if (EnvironmentHelper.IsWindows8OrBetter && isModernStartMenuOpen())
            {
                // Windows 8+
                newIsVisible = true;
                startHmonitor = relocateAndHMonitorModernStartMenu();
            }

            if (!newIsVisible && isClassicStartMenuOpen())
            {
                // Windows 7, StartIsBack, Start8+
                newIsVisible = true;
                startHmonitor = hMonitorClassicStartMenu();
            }

            if (!newIsVisible && isOpenShellMenuOpen())
            {
                // Open Shell Menu
                newIsVisible = true;
                relocateStartMenuByClass("OpenShell.CMenuContainer");
                startHmonitor = hMonitorOpenShellMenu();
            }

            setVisibility(newIsVisible, startHmonitor);
        }

        private void setVisibility(bool isVisible, IntPtr startHmonitor)
        {
            if (isVisible == _isVisible)
            {
                return;
            }

            _isVisible = isVisible;

            StartMenuMonitorEventArgs args = new StartMenuMonitorEventArgs
            {
                Visible = _isVisible,
                TaskbarHwndActivated = _taskbarHwndActivated,
                StartHmonitor = startHmonitor
            };

            StartMenuVisibilityChanged?.Invoke(this, args);

            if (_taskbarHwndActivated != IntPtr.Zero)
            {
                // Now that it has been consumed, reset to prevent sending stale data
                // if the menu is opened again not by the start button.
                _taskbarHwndActivated = IntPtr.Zero;
            }
        }

        private bool isModernStartMenuOpen()
        {
            if (_appVisibilityHelper == null)
            {
                ShellLogger.Error("StartMenuMonitor: AppVisibilityHelper is null");
                return false;
            }

            return _appVisibilityHelper.IsLauncherVisible();
        }

        private bool isClassicStartMenuOpen()
        {
            return isVisibleByClass("DV2ControlHost");
        }

        private bool isOpenShellMenuOpen()
        {
            return isVisibleByClass("OpenShell.CMenuContainer");
        }

        private bool isVisibleByClass(string className)
        {
            IntPtr hStartMenu = FindWindowEx(IntPtr.Zero, IntPtr.Zero, className, IntPtr.Zero);

            if (hStartMenu == IntPtr.Zero)
            {
                return false;
            }

            return IsWindowVisible(hStartMenu);
        }

        private IntPtr relocateAndHMonitorModernStartMenu()
        {
            IntPtr hwndForeground = GetForegroundWindow();
            StringBuilder cName = new StringBuilder(256);
            GetClassName(hwndForeground, cName, cName.Capacity);
            ShellLogger.Debug($"StartMenuMonitor DIAG: poller tick, isModernStartMenuOpen=true, foreground hwnd={hwndForeground}, class={cName}");
            if (cName.ToString() == "Windows.UI.Core.CoreWindow")
            {
                // When the modern Start menu opens, it gains focus, so this is probably it.
                // Unlike Open Shell, the OS doesn't know where our custom Start button is, so
                // it opens wherever it assumes the (hidden) system taskbar's button is. Correct
                // that the same way relocateStartMenuByClass does for Open Shell.
                relocateStartMenu(hwndForeground);
                return MonitorFromWindow(hwndForeground, MONITOR_DEFAULTTONEAREST);
            }
            return IntPtr.Zero;
        }

        private IntPtr hMonitorClassicStartMenu()
        {
            return hMonitorByClass("DV2ControlHost");
        }

        private IntPtr hMonitorOpenShellMenu()
        {
            return hMonitorByClass("OpenShell.CMenuContainer");
        }

        private IntPtr hMonitorByClass(string className)
        {
            IntPtr hStartMenu = FindWindowEx(IntPtr.Zero, IntPtr.Zero, className, IntPtr.Zero);

            if (hStartMenu == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return MonitorFromWindow(hStartMenu, MONITOR_DEFAULTTONEAREST);
        }

        private void relocateStartMenuByClass(string className)
        {
            IntPtr hStartMenu = FindWindowEx(IntPtr.Zero, IntPtr.Zero, className, IntPtr.Zero);
            if (hStartMenu == IntPtr.Zero)
            {
                return;
            }

            relocateStartMenu(hStartMenu);
        }

        private void relocateStartMenu(IntPtr hStartMenu)
        {
            if (_positionedMenu == hStartMenu && _correctPlacement != null)
            {
                CorrectPlacement();
                return;
            }
            if (_taskbarHwndActivated == IntPtr.Zero)
            {
                return;
            }

            FlowDirection flowDirection = Application.Current.FindResource("flow_direction") as FlowDirection? ?? FlowDirection.LeftToRight;
            GetWindowRect(hStartMenu, out ManagedShell.Interop.NativeMethods.Rect startMenuRect);
            GetWindowRect(_taskbarHwndActivated, out ManagedShell.Interop.NativeMethods.Rect taskbarRect);
            ShellLogger.Debug($"StartMenuMonitor DIAG: relocateStartMenu entered, hStartMenu={hStartMenu}, currentRect=({startMenuRect.Left},{startMenuRect.Top},{startMenuRect.Right},{startMenuRect.Bottom}), taskbarRect=({taskbarRect.Left},{taskbarRect.Top},{taskbarRect.Right},{taskbarRect.Bottom})");

            // Use the edge of whichever taskbar the button was actually pressed on, not
            // the primary one — with multiple taskbars they can differ.
            AppBarEdge edge = Settings.Instance.Edge;
            foreach (UltraWinBar.Taskbar taskbar in Application.Current.Windows.OfType<UltraWinBar.Taskbar>())
            {
                if (taskbar.Handle == _taskbarHwndActivated)
                {
                    edge = taskbar.AppBarEdge;
                    break;
                }
            }

            var screen = System.Windows.Forms.Screen.FromHandle(_taskbarHwndActivated);
            var area = WindowPlacementGuard.AvailableArea(screen.Bounds);
            Point initialTarget = StartMenuPlacement.GetTarget(startMenuRect, taskbarRect, area, edge, flowDirection == FlowDirection.RightToLeft);
            int x = (int)initialTarget.X, y = (int)initialTarget.Y;

            bool menuAlreadyAtTarget = y == startMenuRect.Top && x == startMenuRect.Left;
            if (!menuAlreadyAtTarget && startMenuRect.Width > 200 && startMenuRect.Height > 200)
            {
                bool moved = SetWindowPos(hStartMenu, IntPtr.Zero, x, y, 0, 0, (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                ShellLogger.Debug($"StartMenuMonitor DIAG: SetWindowPos target=({x},{y}) returned {moved}");
            }

            // Open Shell first exposes a 101x100 placeholder, then creates its full menu and
            // the separate user-picture HWND. Capture their original relationship once both
            // exist, then keep applying absolute targets so retries cannot accumulate drift.
            ManagedShell.Interop.NativeMethods.Rect referenceMenuRect = startMenuRect;
            bool hasReferenceMenuRect = startMenuRect.Width > 200 && startMenuRect.Height > 200;
            bool hasUserPictureOffset = false;
            int userPictureOffsetX = 0;
            int userPictureOffsetY = 0;
            _positionedMenu = hStartMenu;
            _correctPlacement = () =>
            {
                if (!IsWindow(hStartMenu))
                {
                    _correctPlacement = null;
                    _positionedMenu = IntPtr.Zero;
                    return;
                }

                GetWindowRect(hStartMenu, out ManagedShell.Interop.NativeMethods.Rect currentRect);
                if (currentRect.Width <= 200 || currentRect.Height <= 200) return;
                Point target = StartMenuPlacement.GetTarget(currentRect, taskbarRect,
                    WindowPlacementGuard.AvailableArea(screen.Bounds), edge, flowDirection == FlowDirection.RightToLeft);
                x = (int)target.X;
                y = (int)target.Y;
                if ((currentRect.Left != x || currentRect.Top != y) &&
                    currentRect.Width > 200 && currentRect.Height > 200)
                {
                    referenceMenuRect = currentRect;
                    hasReferenceMenuRect = true;
                }

                if (currentRect.Left != x || currentRect.Top != y)
                {
                    ShellLogger.Debug($"StartMenuMonitor DIAG: Start menu drifted to ({currentRect.Left},{currentRect.Top}), re-applying target=({x},{y})");
                    SetWindowPos(hStartMenu, IntPtr.Zero, x, y, 0, 0, (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                }

                IntPtr hUserPicture = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "OpenShell.CUserWindow", IntPtr.Zero);
                if (hUserPicture == IntPtr.Zero)
                {
                    hUserPicture = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Desktop User Picture", IntPtr.Zero);
                }
                if (!hasUserPictureOffset && hasReferenceMenuRect && hUserPicture != IntPtr.Zero)
                {
                    GetWindowRect(hUserPicture, out ManagedShell.Interop.NativeMethods.Rect userPictureRect);
                    bool userPictureIsPositioned = userPictureRect.Left >= referenceMenuRect.Left &&
                        userPictureRect.Left < referenceMenuRect.Right &&
                        userPictureRect.Top >= referenceMenuRect.Top &&
                        userPictureRect.Top < referenceMenuRect.Bottom;
                    if (userPictureIsPositioned)
                    {
                        userPictureOffsetX = userPictureRect.Left - referenceMenuRect.Left;
                        userPictureOffsetY = userPictureRect.Top - referenceMenuRect.Top;
                        hasUserPictureOffset = true;
                        ShellLogger.Debug($"StartMenuMonitor DIAG: captured user-picture offset=({userPictureOffsetX},{userPictureOffsetY})");
                    }
                }

                if (hasUserPictureOffset && hUserPicture != IntPtr.Zero)
                {
                    GetWindowRect(hUserPicture, out var pictureRect);
                    if (pictureRect.Left != x + userPictureOffsetX || pictureRect.Top != y + userPictureOffsetY)
                        SetWindowPos(hUserPicture, IntPtr.Zero, x + userPictureOffsetX, y + userPictureOffsetY, 0, 0,
                            (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                }

            };
            CorrectPlacement();
        }

        private IImmersiveMonitor GetImmersiveMonitor(ManagedShell.UWPInterop.Interfaces.IServiceProvider shell, IntPtr hWnd)
        {
            if (shell.QueryService(ref CLSID_ImmersiveMonitorManager, ref IID_ImmersiveMonitorManager, out object monitorManagerObj) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to query for IImmersiveMonitorManager");
                return null;
            }
            IImmersiveMonitorManager monitorManager = (IImmersiveMonitorManager)monitorManagerObj;

            if (monitorManager.GetFromHandle(MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST), out IImmersiveMonitor monitor) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to get monitor from taskbar window handle");
                return null;
            }

            return monitor;
        }

        // Re-querying and reconnecting these COM launchers is 2 out-of-process round trips;
        // caching per connected monitor avoids paying that cost on every Start button press.
        private IImmersiveLauncher_Win10RS1 _cachedLauncherRS1;
        private IImmersiveLauncher_Win81 _cachedLauncherWin81;
        private IntPtr _cachedLauncherMonitor = IntPtr.Zero;

        private IImmersiveLauncher_Win10RS1 GetImmersiveLauncher_Win10RS1(IntPtr taskbarHwnd)
        {
            IntPtr targetMonitor = MonitorFromWindow(taskbarHwnd, MONITOR_DEFAULTTONEAREST);
            if (_cachedLauncherRS1 != null && _cachedLauncherMonitor == targetMonitor)
            {
                return _cachedLauncherRS1;
            }

            var shell = ImmersiveShellHelper.GetImmersiveShell();
            if (shell.QueryService(ref CLSID_ImmersiveLauncher, ref IID_ImmersiveLauncher_Win10RS1, out object immersiveLauncherObj) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to query for IImmersiveLauncher_Win10RS1");
                return null;
            }
            IImmersiveLauncher_Win10RS1 immersiveLauncher = (IImmersiveLauncher_Win10RS1)immersiveLauncherObj;

            IImmersiveMonitor monitor = GetImmersiveMonitor(shell, taskbarHwnd);
            if (monitor == null || immersiveLauncher.ConnectToMonitor(monitor) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to connect IImmersiveLauncher_Win10RS1 to monitor");
                return null;
            }

            _cachedLauncherRS1 = immersiveLauncher;
            _cachedLauncherMonitor = targetMonitor;
            return immersiveLauncher;
        }

        private IImmersiveLauncher_Win81 GetImmersiveLauncher_Win81(IntPtr taskbarHwnd)
        {
            IntPtr targetMonitor = MonitorFromWindow(taskbarHwnd, MONITOR_DEFAULTTONEAREST);
            if (_cachedLauncherWin81 != null && _cachedLauncherMonitor == targetMonitor)
            {
                return _cachedLauncherWin81;
            }

            var shell = ImmersiveShellHelper.GetImmersiveShell();
            if (shell.QueryService(ref CLSID_ImmersiveLauncher, ref IID_ImmersiveLauncher_Win81, out object immersiveLauncherObj) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to query for IImmersiveLauncher_Win81");
                return null;
            }
            IImmersiveLauncher_Win81 immersiveLauncher = (IImmersiveLauncher_Win81)immersiveLauncherObj;

            IImmersiveMonitor monitor = GetImmersiveMonitor(shell, taskbarHwnd);
            if (monitor == null || immersiveLauncher.ConnectToMonitor(monitor) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to connect IImmersiveLauncher_Win81 to monitor");
                return null;
            }

            _cachedLauncherWin81 = immersiveLauncher;
            _cachedLauncherMonitor = targetMonitor;
            return immersiveLauncher;
        }

        private bool TryOpenShellDirectInvoke()
        {
            IntPtr owner = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "OpenShell.COwnerWindow", IntPtr.Zero);
            if (owner == IntPtr.Zero) return false;
            GetWindowThreadProcessId(owner, out uint ownerProcess);
            if (ownerProcess == 0) return false;
            IntPtr tray = IntPtr.Zero;
            while ((tray = FindWindowEx(IntPtr.Zero, tray, "Shell_TrayWnd", IntPtr.Zero)) != IntPtr.Zero)
            {
                GetWindowThreadProcessId(tray, out uint trayProcess);
                if (trayProcess != ownerProcess) continue;
                int message = RegisterWindowMessage("OpenShellMenu.StartMenuMsg");
                if (message == 0) return false;
                AllowSetForegroundWindow(ownerProcess);
                bool posted = PostMessage(tray, (uint)message, (IntPtr)2, IntPtr.Zero);
                ShellLogger.Debug($"StartMenuMonitor: Open-Shell request posted={posted}, host={ownerProcess}, tray={tray}");
                return posted;
            }
            return false;
        }

        internal void ShowStartMenu(IntPtr taskbarHwnd)
        {
            var diagStopwatch = System.Diagnostics.Stopwatch.StartNew();
            ShellLogger.Debug("StartMenuMonitor DIAG: ShowStartMenu entered");
            _taskbarHwndActivated = taskbarHwnd;
            if (TryOpenShellDirectInvoke()) return;

            if (!EnvironmentHelper.IsWindows10OrBetter ||
                FindWindowEx(IntPtr.Zero, IntPtr.Zero, "OpenShell.COwnerWindow", IntPtr.Zero) != IntPtr.Zero ||
                FindWindowEx(IntPtr.Zero, IntPtr.Zero, "DV2ControlHost", IntPtr.Zero) != IntPtr.Zero)
            {
                // Invoke once, without a delayed second request that can toggle the menu closed.
                ShellLogger.Debug("StartMenuMonitor DIAG: falling back to ShellHelper.ShowStartMenu (SendInput) — OpenShell/DV2ControlHost detected or pre-Win10");
                ShellHelper.ShowStartMenu();
                return;
            }

            try
            {
                // Allow Explorer to steal focus
                GetWindowThreadProcessId(FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", "Program Manager"), out uint procId);
                AllowSetForegroundWindow(procId);

                if (EnvironmentHelper.IsWindows10RS1OrBetter)
                {
                    IImmersiveLauncher_Win10RS1 immersiveLauncher = GetImmersiveLauncher_Win10RS1(taskbarHwnd);
                    ShellLogger.Debug($"StartMenuMonitor DIAG: GetImmersiveLauncher_Win10RS1 returned {(immersiveLauncher != null ? "non-null" : "null")} at {diagStopwatch.ElapsedMilliseconds}ms");
                    if (immersiveLauncher != null)
                    {
                        int hr = immersiveLauncher.ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD.ILSM_STARTBUTTON, IMMERSIVELAUNCHERSHOWFLAGS.ILSF_IGNORE_SET_FOREGROUND_ERROR);
                        ShellLogger.Debug($"StartMenuMonitor DIAG: ShowStartView returned hr={hr} at {diagStopwatch.ElapsedMilliseconds}ms");
                        if (hr == 0)
                        {
                            return;
                        }
                    }
                }
                else
                {
                    IImmersiveLauncher_Win81 immersiveLauncher = GetImmersiveLauncher_Win81(taskbarHwnd);
                    if (immersiveLauncher != null &&
                        immersiveLauncher.ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD.ILSM_STARTBUTTON, IMMERSIVELAUNCHERSHOWFLAGS.ILSF_IGNORE_SET_FOREGROUND_ERROR) == 0)
                    {
                        return;
                    }
                }
                ShellLogger.Warning("StartMenuMonitor: Failed to show Start menu via IImmersiveLauncher");
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"StartMenuMonitor: Failed to show Start menu via IImmersiveLauncher: {e}");

                // The cached launcher may be a stale RCW (e.g. explorer.exe restarted);
                // drop it so the next press re-queries a fresh one instead of failing forever.
                _cachedLauncherRS1 = null;
                _cachedLauncherWin81 = null;
                _cachedLauncherMonitor = IntPtr.Zero;
            }

            ShellHelper.ShowStartMenu();
        }

        public void Dispose()
        {
            _poller?.Stop();
            _correctPlacement = null;
            if (_menuEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_menuEventHook);
                _menuEventHook = IntPtr.Zero;
            }

            if (_foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundEventHook);
                _foregroundEventHook = IntPtr.Zero;
            }
        }

        #region Immersive launcher interfaces

        // Most managed interface definitions c/o https://github.com/MishaProductions/CustomShell/

        enum IMMERSIVE_MONITOR_FILTER_FLAGS
        {
            IMMERSIVE_MONITOR_FILTER_FLAGS_NONE = 0x0,
            IMMERSIVE_MONITOR_FILTER_FLAGS_DISABLE_TRAY = 0x1,
        }

        [ComImport]
        [Guid("880b26f8-9197-43d0-8045-8702d0d72000")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveMonitor
        {
            public int GetIdentity(out uint pIdentity);
            public int Append(object unknown);
            public int GetHandle(out nint phMonitor);
            public int IsConnected(out bool pfConnected);
            public int IsPrimary(out bool pfPrimary);
            public int GetTrustLevel(out uint level);
            public int GetDisplayRect(out ManagedShell.Interop.NativeMethods.Rect prcDisplayRect);
            public int GetOrientation(out uint pdwOrientation);
            public int GetWorkArea(out ManagedShell.Interop.NativeMethods.Rect prcWorkArea);
            public int IsEqual(IImmersiveMonitor pMonitor, out bool pfEqual);
            public int GetTrustLevel2(out uint level);
            public int GetEffectiveDpi(out uint dpiX, out uint dpiY);
            public int GetFilterFlags(out IMMERSIVE_MONITOR_FILTER_FLAGS flags);
        }

        enum IMMERSIVE_MONITOR_MOVE_DIRECTION
        {
            IMMD_PREVIOUS = 0,
            IMMD_NEXT = 1
        }

        [ComImport]
        [Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveMonitorManager
        {
            public int GetCount(out uint pcMonitors);
            public int GetConnectedCount(out uint pcMonitors);
            public int GetAt(uint idxMonitor, out IImmersiveMonitor monitor);
            public int GetFromHandle(nint monitor, out IImmersiveMonitor monitor2);
            public int GetFromIdentity(uint identity, out IImmersiveMonitor monitor);
            public int GetImmersiveProxyMonitor(out IImmersiveMonitor monitor);
            public int QueryService(nint monit, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int QueryServiceByIdentity(uint monit, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int QueryServiceFromWindow(nint hwnd, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int QueryServiceFromPoint(nint point, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int GetNextImmersiveMonitor(IMMERSIVE_MONITOR_MOVE_DIRECTION direction, IImmersiveMonitor monitor, out IImmersiveMonitor monitorout);
            public int GetMonitorArray(out object array);
            public int SetFilter(object filter);
        }

        enum IMMERSIVELAUNCHERSHOWMETHOD
        {
            ILSM_INVALID = 0x0,
            ILSM_HSHELLTASKMAN = 0x1,
            ILSM_IMMERSIVEBACKGROUND = 0x4,
            ILSM_APPCLOSED = 0x6,
            ILSM_STARTBUTTON = 0xB,
            ILSM_RETAILDEMO_EDUCATIONAPP = 0xC,
            ILSM_BACK = 0xD,
            ILSM_SESSIONONUNLOCK = 0xE
        }

        enum IMMERSIVELAUNCHERSHOWFLAGS
        {
            ILSF_NONE = 0x0,
            ILSF_IGNORE_SET_FOREGROUND_ERROR = 0x4,
        }

        enum IMMERSIVELAUNCHERDISMISSMETHOD
        {
            ILDM_INVALID = 0x0,
            ILDM_HSHELLTASKMAN = 0x1,
            ILDM_STARTCHARM = 0x2,
            ILDM_BACKGESTURE = 0x3,
            ILDM_ESCAPEKEY = 0x4,
            ILDM_SHOWDESKTOP = 0x5,
            ILDM_STARTTIP = 0x6,
            ILDM_GENERIC_NONANIMATING = 0x7,
        }

        [ComImport]
        [Guid("d8d60399-a0f1-f987-5551-321fd1b49864")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveLauncher_Win10RS1
        {
            public int ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERSHOWFLAGS showFlags);
            public int Dismiss(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int Dismiss2(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int DismissSynchronouslyWithoutTransition();
            public int IsVisible(out bool p0);
            public int OnStartButtonPressed(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int SetForeground();
            public int ConnectToMonitor(IImmersiveMonitor monitor);
            public int GetMonitor(out IImmersiveMonitor monitor);
            public int OnFirstSignAnimationFinished();
            public int Prelaunch();
        }

        [ComImport]
        [Guid("93f91f5a-a4ca-4205-9beb-ce4d17c708f9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveLauncher_Win81
        {
            public int ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERSHOWFLAGS showFlags);
            public int Unknown2();
            public int Unknown3();
            public int Unknown4();
            public int Unknown5();
            public int Dismiss(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int Unknown7();
            public int IsVisible(out bool p0);
            public int Unknown9();
            public int Unknown10();
            public int Unknown11();
            public int Unknown12();
            public int Unknown13();
            public int Unknown14();
            public int Unknown15();
            public int ConnectToMonitor(IImmersiveMonitor monitor);
            public int GetMonitor(out IImmersiveMonitor monitor);
        }

        static Guid CLSID_ImmersiveMonitorManager = new Guid("47094e3a-0cf2-430f-806f-cf9e4f0f12dd");
        static Guid IID_ImmersiveMonitorManager = new Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2");
        static Guid CLSID_ImmersiveLauncher = new Guid("6f86e01c-c649-4d61-be23-f1322ddeca9d");
        static Guid IID_ImmersiveLauncher_Win10RS1 = new Guid("d8d60399-a0f1-f987-5551-321fd1b49864");
        static Guid IID_ImmersiveLauncher_Win81 = new Guid("93f91f5a-a4ca-4205-9beb-ce4d17c708f9");
        #endregion

        public class StartMenuMonitorEventArgs : LauncherVisibilityEventArgs
        {
            public IntPtr TaskbarHwndActivated;
            public IntPtr StartHmonitor;
        }
    }
}
