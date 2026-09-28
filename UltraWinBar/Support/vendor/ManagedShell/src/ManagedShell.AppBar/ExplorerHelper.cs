using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTray;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using ManagedShell.Common.Logging;
using static ManagedShell.Interop.NativeMethods;

namespace ManagedShell.AppBar
{
    // UltraWinBar: IDisposable so the WinEvent hook below is guaranteed to be unhooked
    public class ExplorerHelper : IDisposable
    {
        private static ABState? startupTaskbarState;
        internal NotificationArea _notificationArea;


        // UltraWinBar: was a 100ms poll; now just a safety net behind the WinEvent hook
        private readonly DispatcherTimer taskbarMonitor = new DispatcherTimer(DispatcherPriority.Background);

        // UltraWinBar: WinEvent hook fields for event-driven taskbar-show detection
        private IntPtr taskbarShowHook = IntPtr.Zero;
        private WinEventProc taskbarShowEventProc;
        private bool _hideCheckPending;

        private bool _hideExplorerTaskbar;

        public bool HideExplorerTaskbar
        {
            get => _hideExplorerTaskbar;

            set
            {
                if (value != _hideExplorerTaskbar && !EnvironmentHelper.IsAppRunningAsShell)
                {
                    _hideExplorerTaskbar = value;

                    if (_hideExplorerTaskbar)
                    {
                        HideTaskbar();
                    }
                    else
                    {
                        ShowTaskbar();
                    }
                }
            }
        }

        public ExplorerHelper() : this(null)
        {
        }

        public ExplorerHelper(NotificationArea notificationArea)
        {
            _notificationArea = notificationArea;

            SetupTaskbarMonitor();
        }

        public void SetTaskbarVisibility(int swp)
        {
            // only run this if our TaskBar is enabled, or if we are showing the Windows TaskBar
            if (swp != (int)SetWindowPosFlags.SWP_HIDEWINDOW || HideExplorerTaskbar)
            {
                IntPtr taskbarHwnd = WindowHelper.FindWindowsTray(getNotifyAreaHandle());
                IntPtr startButtonHwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, (IntPtr)0xC017, null);

                if (taskbarHwnd != IntPtr.Zero
                    && swp == (int)SetWindowPosFlags.SWP_HIDEWINDOW == IsWindowVisible(taskbarHwnd))
                {
                    SetWindowPos(taskbarHwnd, (IntPtr)WindowZOrder.HWND_BOTTOM, 0, 0, 0, 0, swp | (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOSIZE | (int)SetWindowPosFlags.SWP_NOACTIVATE);
                    if (startButtonHwnd != IntPtr.Zero)
                    {
                        SetWindowPos(startButtonHwnd, (IntPtr)WindowZOrder.HWND_BOTTOM, 0, 0, 0, 0, swp | (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOSIZE | (int)SetWindowPosFlags.SWP_NOACTIVATE);
                    }
                }

                // adjust secondary TaskBars for multi-monitor
                SetSecondaryTaskbarVisibility(swp);
            }
        }

        public void SetSecondaryTaskbarVisibility(int swp)
        {
            IntPtr secTaskbarHwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null);

            // if we have 3+ monitors there may be multiple secondary TaskBars
            while (secTaskbarHwnd != IntPtr.Zero)
            {
                if (swp == (int)SetWindowPosFlags.SWP_HIDEWINDOW == IsWindowVisible(secTaskbarHwnd))
                {
                    SetWindowPos(secTaskbarHwnd, (IntPtr)WindowZOrder.HWND_BOTTOM, 0, 0, 0, 0, swp | (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOSIZE | (int)SetWindowPosFlags.SWP_NOACTIVATE);
                }

                secTaskbarHwnd = FindWindowEx(IntPtr.Zero, secTaskbarHwnd, "Shell_SecondaryTrayWnd", null);
            }
        }

        public void SetTaskbarState(ABState state)
        {
            APPBARDATA abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(APPBARDATA)),
                hWnd = WindowHelper.FindWindowsTray(getNotifyAreaHandle()),
                lParam = (IntPtr)state
            };

            SHAppBarMessage((int)ABMsg.ABM_SETSTATE, ref abd);
        }

        public ABState GetTaskbarState()
        {
            APPBARDATA abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(APPBARDATA)),
                hWnd = WindowHelper.FindWindowsTray(getNotifyAreaHandle())
            };

            uint uState = SHAppBarMessage((int)ABMsg.ABM_GETSTATE, ref abd);

            return (ABState)uState;
        }

        private IntPtr getNotifyAreaHandle()
        {
            if (_notificationArea == null) return IntPtr.Zero;

            return _notificationArea.Handle;
        }

        private void HideTaskbar()
        {
            if (startupTaskbarState == null)
            {
                startupTaskbarState = GetTaskbarState();
            }

            if (HideExplorerTaskbar)
            {
                DoHideTaskbar();
                taskbarMonitor.Start();
                InstallTaskbarShowHook(); // UltraWinBar: start event-driven detection alongside the safety timer
            }
        }

        private void DoHideTaskbar()
        {
            SetTaskbarState(ABState.AutoHide);
            SetTaskbarVisibility((int)SetWindowPosFlags.SWP_HIDEWINDOW);
        }

        private void ShowTaskbar()
        {
            SetTaskbarState(startupTaskbarState ?? ABState.Default);
            SetTaskbarVisibility((int)SetWindowPosFlags.SWP_SHOWWINDOW);
            taskbarMonitor.Stop();
            UninstallTaskbarShowHook(); // UltraWinBar: unhook exactly where the timer used to stop
        }

        private void SetupTaskbarMonitor()
        {
            // UltraWinBar: 100ms poll -> 2s safety net; the WinEvent hook does real-time detection now
            taskbarMonitor.Interval = new TimeSpan(0, 0, 2);
            taskbarMonitor.Tick += TaskbarMonitor_Tick;
        }

        // UltraWinBar: install a WinEvent hook for EVENT_OBJECT_SHOW instead of polling every 100ms
        private void InstallTaskbarShowHook()
        {
            if (taskbarShowHook != IntPtr.Zero)
            {
                return;
            }

            taskbarShowEventProc = TaskbarShowEventCallback;
            taskbarShowHook = SetWinEventHook(
                EVENT_OBJECT_SHOW,
                EVENT_OBJECT_SHOW,
                IntPtr.Zero,
                taskbarShowEventProc,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        // UltraWinBar: unhook on the installing (UI) thread; keep the delegate rooted until then
        private void UninstallTaskbarShowHook()
        {
            if (taskbarShowHook == IntPtr.Zero)
            {
                return;
            }

            UnhookWinEvent(taskbarShowHook);
            taskbarShowHook = IntPtr.Zero;
            taskbarShowEventProc = null;
        }

        // UltraWinBar: WinEvent callback runs on the hooking thread's queue; never let an exception escape into native code
        private void TaskbarShowEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0 || !HideExplorerTaskbar || _hideCheckPending)
                {
                    return;
                }

                // class-name filter also excludes UltraWinBar's own (WPF-classed) windows
                if (!IsExplorerTaskbarWindow(hwnd))
                {
                    return;
                }

                _hideCheckPending = true;
                taskbarMonitor.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _hideCheckPending = false;
                    TaskbarMonitor_Tick(this, EventArgs.Empty);
                }));
            }
            catch (Exception ex)
            {
                ShellLogger.Error("ExplorerHelper: Error handling taskbar show event", ex);
            }
        }

        // UltraWinBar: identify Explorer's primary/secondary taskbar windows by class
        private static bool IsExplorerTaskbarWindow(IntPtr hwnd)
        {
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string name = className.ToString();
            return name == WindowHelper.TrayWndClass || name == "Shell_SecondaryTrayWnd";
        }

        // UltraWinBar: guarantee the hook is removed even if HideExplorerTaskbar was never toggled off
        public void Dispose()
        {
            taskbarMonitor.Stop();
            UninstallTaskbarShowHook();
        }

        private void TaskbarMonitor_Tick(object sender, EventArgs e)
        {
            IntPtr taskbarHwnd = WindowHelper.FindWindowsTray(getNotifyAreaHandle());

            if (IsWindowVisible(taskbarHwnd))
            {
                ShellLogger.Debug("ExplorerHelper: Hiding unwanted Windows taskbar");
                DoHideTaskbar();
                return;
            }

            IntPtr secTaskbarHwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null);

            // if we have 3+ monitors there may be multiple secondary TaskBars
            while (secTaskbarHwnd != IntPtr.Zero)
            {
                if (IsWindowVisible(secTaskbarHwnd))
                {
                    ShellLogger.Debug("ExplorerHelper: Hiding unwanted Windows taskbar");
                    DoHideTaskbar();
                    return;
                }

                secTaskbarHwnd = FindWindowEx(IntPtr.Zero, secTaskbarHwnd, "Shell_SecondaryTrayWnd", null);
            }
        }
    }
}