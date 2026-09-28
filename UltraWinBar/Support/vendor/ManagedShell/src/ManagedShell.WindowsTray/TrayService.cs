using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace ManagedShell.WindowsTray
{
    public class TrayService : IDisposable
    {
        private const string NotifyWndClass = "TrayNotifyWnd";
        private const string TrayWndClass = "Shell_TrayWnd";
        private readonly int[] ForwardMessagesPost = { (int)WM.USER + 372 };

        private AppBarMessageDelegate appBarMessageDelegate;
        private IconDataDelegate iconDataDelegate;
        private SystrayDelegate trayDelegate;
        private WndProcDelegate wndProcDelegate;

        private IntPtr HwndTray;
        private IntPtr HwndNotify;
        private IntPtr HwndFwd;
        private IntPtr hInstance = Marshal.GetHINSTANCE(typeof(TrayService).Module);

        // UltraWinBar: slow safety net only; the real-time watch is the WinEvent hook below.
        private readonly DispatcherTimer trayMonitor = new DispatcherTimer(DispatcherPriority.Background);

        // UltraWinBar: event-driven replacement for the old 100 ms poll. Hooked/unhooked together
        // with the safety timer so both always match Suspend/Resume state.
        private WinEventProc trayEventProc;
        private IntPtr trayObjectEventHook = IntPtr.Zero;
        private IntPtr trayForegroundEventHook = IntPtr.Zero;
        private bool trayCheckQueued;

        // UltraWinBar: WndProc exception barrier rate limit.
        private const int WndProcFailureLogLimit = 20;
        private static int wndProcFailureCount;

        // UltraWinBar: cross-process forward to Explorer's tray must not block on a hung Explorer.
        private const uint ForwardTimeoutMs = 500;

        public TrayService()
        {
            SetupTrayMonitor();
        }

        #region Set callbacks
        internal void SetSystrayCallback(SystrayDelegate theDelegate)
        {
            trayDelegate = theDelegate;
        }

        internal void SetIconDataCallback(IconDataDelegate theDelegate)
        {
            iconDataDelegate = theDelegate;
        }

        internal void SetAppBarMessageCallback(AppBarMessageDelegate theDelegate)
        {
            appBarMessageDelegate = theDelegate;
        }
        #endregion

        internal IntPtr Initialize()
        {
            if (HwndTray != IntPtr.Zero)
            {
                return HwndTray;
            }

            DestroyWindows();

            // UltraWinBar: reuse the rooted delegate across retries. If a previous CreateWindowEx
            // failed after RegisterClass succeeded, that class may still be registered with the
            // old delegate; swapping it here would leave a dangling thunk behind.
            if (wndProcDelegate == null)
            {
                wndProcDelegate = WndProc;
            }

            RegisterTrayWnd();
            RegisterNotifyWnd();

            return HwndTray;
        }

        /// <summary>
        /// Starts the system tray listener (send the TaskbarCreated message).
        /// </summary>
        internal void Run()
        {
            if (HwndTray != IntPtr.Zero)
            {
                Resume();
                SendTaskbarCreated();
            }
        }

        internal void Suspend()
        {
            // if we go beneath another tray, it will receive messages
            if (HwndTray != IntPtr.Zero)
            {
                trayMonitor.Stop();
                UnhookTrayEvents();
                SetWindowPos(HwndTray, (IntPtr)WindowZOrder.HWND_BOTTOM, 0, 0, 0, 0,
                    (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOACTIVATE |
                    (int)SetWindowPosFlags.SWP_NOSIZE);
            }
        }

        internal void Resume()
        {
            // if we are above another tray, we will receive messages
            if (HwndTray != IntPtr.Zero)
            {
                SetWindowsTrayBottommost();
                MakeTrayTopmost();
                HookTrayEvents();
                trayMonitor.Start();
            }
        }

        internal void SetTrayHostSizeData(TrayHostSizeData data)
        {
            if (HwndTray != IntPtr.Zero)
            {
                SetWindowPos(HwndTray, IntPtr.Zero, data.rc.Left, data.rc.Top, data.rc.Width, data.rc.Height, (int)SetWindowPosFlags.SWP_NOACTIVATE | (int)SetWindowPosFlags.SWP_NOZORDER);
            }

            if (HwndNotify != IntPtr.Zero)
            {
                SetWindowPos(HwndNotify, IntPtr.Zero, data.rc.Left, data.rc.Top, data.rc.Width, data.rc.Height, (int)SetWindowPosFlags.SWP_NOACTIVATE | (int)SetWindowPosFlags.SWP_NOZORDER);
            }
        }

        private void SendTaskbarCreated()
        {
            int msg = RegisterWindowMessage("TaskbarCreated");

            if (msg > 0)
            {
                ShellLogger.Debug("TrayService: Sending TaskbarCreated message");
                SendNotifyMessage(HWND_BROADCAST,
                    (uint)msg, UIntPtr.Zero, IntPtr.Zero);
            }
        }

        private void DestroyWindows()
        {
            if (HwndNotify != IntPtr.Zero)
            {
                DestroyWindow(HwndNotify);
                UnregisterClass(NotifyWndClass, hInstance);
                ShellLogger.Debug($"TrayService: Unregistered {NotifyWndClass}");
                HwndNotify = IntPtr.Zero;
            }

            if (HwndTray != IntPtr.Zero)
            {
                DestroyWindow(HwndTray);
                UnregisterClass(TrayWndClass, hInstance);
                ShellLogger.Debug($"TrayService: Unregistered {TrayWndClass}");
                HwndTray = IntPtr.Zero;
            }

            HwndFwd = IntPtr.Zero;
        }

        public void Dispose()
        {
            // UltraWinBar: App.ExitApp runs twice on session end (SessionEnding then Exit); make
            // a second Dispose a no-op instead of destroying stale handles and re-broadcasting
            // TaskbarCreated. The monitor and hooks are stopped regardless: they run even if Initialize never did.
            trayMonitor.Stop();
            UnhookTrayEvents();
            if (HwndTray == IntPtr.Zero && HwndNotify == IntPtr.Zero)
            {
                return;
            }

            DestroyWindows();

            if (!EnvironmentHelper.IsAppRunningAsShell)
                SendTaskbarCreated();
        }

        // UltraWinBar: registered with user32 via RegisterClass; every Shell_NotifyIcon and
        // SHAppBarMessage in the session arrives here. An exception escaping this delegate goes
        // straight into user32 (process-fatal on net10, undefined on net6) - never let one out.
        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                return WndProcCore(hWnd, msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                ReportWndProcFailure(ex);
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }

        private static void ReportWndProcFailure(Exception ex)
        {
            int count = Interlocked.Increment(ref wndProcFailureCount);
            if (count <= WndProcFailureLogLimit)
            {
                ShellLogger.Error($"TrayService: WndProc failed ({count})", ex);
            }
            else if (count == WndProcFailureLogLimit + 1)
            {
                ShellLogger.Error("TrayService: further WndProc failures are not logged.");
            }
        }

        private IntPtr WndProcCore(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            switch ((WM)msg)
            {
                case WM.COPYDATA:
                    if (lParam == IntPtr.Zero)
                    {
                        ShellLogger.Debug("TrayService: CopyData is null");
                        break;
                    }

                    COPYDATASTRUCT copyData =
                        (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));

                    switch ((int)copyData.dwData)
                    {
                        case 0:
                            // AppBar message
                            if (CrossProcessMessages.TryReadAppBarMessage(copyData, out APPBARMSGDATAV3 amd))
                            {
                                bool handled = false;
                                IntPtr abmResult = IntPtr.Zero;
                                if (appBarMessageDelegate != null)
                                {
                                    abmResult = appBarMessageDelegate(amd, ref handled);
                                }

                                if (handled)
                                {
                                    ShellLogger.Debug($"TrayService: Handled AppBar message {(ABMsg)amd.dwMessage}");
                                    return abmResult;
                                }

                                ShellLogger.Debug($"TrayService: Forwarding AppBar message {(ABMsg)amd.dwMessage}");
                            }
                            else
                            {
                                ShellLogger.Debug("TrayService: AppBar message received, but with unknown size");
                            }
                            break;
                        case 1:
                            // UltraWinBar: any process can send this; never read past its buffer.
                            if (!CrossProcessMessages.TryReadNotifyIconMessage(copyData, out SHELLTRAYDATA trayData))
                            {
                                ShellLogger.Debug("TrayService: Notify icon message with short data ignored");
                                break;
                            }
                            if (trayDelegate != null)
                            {
                                if (trayDelegate(trayData.dwMessage, new SafeNotifyIconData(trayData.nid)))
                                {
                                    return (IntPtr)1;
                                }

                                ShellLogger.Debug("TrayService: Ignored notify icon message");
                            }
                            else
                            {
                                ShellLogger.Info("TrayService: TrayDelegate is null");
                            }
                            break;
                        case 3:
                            if (!CrossProcessMessages.TryReadIconIdentifier(copyData, out WINNOTIFYICONIDENTIFIER iconData))
                            {
                                ShellLogger.Debug("TrayService: Icon identifier message with short data ignored");
                                break;
                            }

                            if (iconDataDelegate != null)
                            {
                                return iconDataDelegate(iconData.dwMessage, iconData.hWnd, iconData.uID,
                                    iconData.guidItem);
                            }

                            ShellLogger.Info("TrayService: IconDataDelegate is null");
                            break;
                    }

                    break;
                case WM.WINDOWPOSCHANGED:
                    WINDOWPOS wndPos = WINDOWPOS.FromMessage(lParam);

                    if ((wndPos.flags & SetWindowPosFlags.SWP_SHOWWINDOW) != 0)
                    {
                        SetWindowLong(HwndTray, WindowLongFlags.GWL_STYLE,
                            GetWindowLong(HwndTray, WindowLongFlags.GWL_STYLE) &
                            ~(int)WindowStyles.WS_VISIBLE);

                        ShellLogger.Debug($"TrayService: {TrayWndClass} became visible; hiding");
                    }

                    // UltraWinBar: our own z-order moved (not just a move/resize); something may
                    // have pushed us out of the topmost band, so re-check instead of waiting for
                    // the safety timer.
                    if (hWnd == HwndTray && (wndPos.flags & SetWindowPosFlags.SWP_NOZORDER) == 0)
                    {
                        ScheduleTrayCheck();
                    }
                    break;
            }

            if (msg == (int)WM.COPYDATA ||
                msg == (int)WM.ACTIVATEAPP ||
                msg == (int)WM.COMMAND ||
                msg >= (int)WM.USER)
            {
                return ForwardMsg(hWnd, msg, wParam, lParam);
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        #region Event handling
        private IntPtr ForwardMsg(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (HwndFwd == IntPtr.Zero || !IsWindow(HwndFwd))
            {
                HwndFwd = WindowHelper.FindWindowsTray(HwndTray);
            }

            if (HwndFwd != IntPtr.Zero)
            {
                if (msg >= (int)WM.USER && ForwardMessagesPost.Contains(msg))
                {
                    ShellLogger.Debug($"TrayService: Forwarding message via PostMessage: {msg}");
                    PostMessage(HwndFwd, (uint)msg, wParam, lParam);
                    return DefWindowProc(hWnd, msg, wParam, lParam);
                }

                // UltraWinBar: don't let a hung Explorer hang our tray's WndProc.
                if (TrySendMessageTimeout(HwndFwd, (uint)msg, wParam, lParam, ForwardTimeoutMs, out IntPtr result))
                {
                    return result;
                }

                ShellLogger.Debug($"TrayService: Forward to Explorer tray timed out or failed: {msg}");
                return IntPtr.Zero;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
        #endregion

        #region Window helpers
        private ushort RegisterWndClass(string name)
        {
            WNDCLASS newClass = new WNDCLASS
            {
                lpszClassName = name,
                hInstance = hInstance,
                style = 0x8,
                lpfnWndProc = wndProcDelegate
            };

            return RegisterClass(ref newClass);
        }

        private void RegisterTrayWnd()
        {
            ushort trayClassReg = RegisterWndClass(TrayWndClass);
            if (trayClassReg == 0)
            {
                ShellLogger.Info($"TrayService: Error registering {TrayWndClass} class ({Marshal.GetLastWin32Error()})");
            }

            HwndTray = CreateWindowEx(
                ExtendedWindowStyles.WS_EX_TOPMOST |
                ExtendedWindowStyles.WS_EX_TOOLWINDOW, trayClassReg, "",
                WindowStyles.WS_POPUP | WindowStyles.WS_CLIPCHILDREN |
                WindowStyles.WS_CLIPSIBLINGS, 0, 0, GetSystemMetrics(0),
                Convert.ToInt32(23 * DpiHelper.DpiScale), IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (HwndTray == IntPtr.Zero)
            {
                ShellLogger.Info($"TrayService: Error creating {TrayWndClass} window ({Marshal.GetLastWin32Error()})");
            }
            else
            {
                ShellLogger.Debug($"TrayService: Created {TrayWndClass}");
            }
        }

        private void RegisterNotifyWnd()
        {
            ushort trayNotifyClassReg = RegisterWndClass(NotifyWndClass);
            if (trayNotifyClassReg == 0)
            {
                ShellLogger.Info($"TrayService: Error registering {NotifyWndClass} class ({Marshal.GetLastWin32Error()})");
            }

            HwndNotify = CreateWindowEx(0, trayNotifyClassReg, null,
                WindowStyles.WS_CHILD | WindowStyles.WS_CLIPCHILDREN |
                WindowStyles.WS_CLIPSIBLINGS, 0, 0, GetSystemMetrics(0),
                Convert.ToInt32(23 * DpiHelper.DpiScale), HwndTray, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (HwndNotify == IntPtr.Zero)
            {
                ShellLogger.Info($"TrayService: Error creating {NotifyWndClass} window ({Marshal.GetLastWin32Error()})");
            }
            else
            {
                ShellLogger.Debug($"TrayService: Created {NotifyWndClass}");
            }
        }

        private void SetupTrayMonitor()
        {
            // UltraWinBar: was a 100 ms poll; the WinEvent hook now does the real-time work and
            // this is only a slow safety net for whatever it misses.
            trayMonitor.Interval = new TimeSpan(0, 0, 2);
            trayMonitor.Tick += TrayMonitor_Tick;
        }

        private void TrayMonitor_Tick(object sender, EventArgs e)
        {
            CheckTrayZOrder();
        }

        private void CheckTrayZOrder()
        {
            if (HwndTray == IntPtr.Zero) return;

            IntPtr taskbarHwnd = FindWindow(TrayWndClass, "");

            if (taskbarHwnd == HwndTray) return;

            ShellLogger.Debug("TrayService: Raising Shell_TrayWnd");
            MakeTrayTopmost();
        }

        // UltraWinBar: install a WinEvent hook so we react to another Shell_TrayWnd appearing
        // instead of polling for it. Must run on the dispatcher thread (message-pump bound hook).
        private void HookTrayEvents()
        {
            if (trayObjectEventHook != IntPtr.Zero || trayForegroundEventHook != IntPtr.Zero) return;

            trayEventProc = TrayWinEventCallback;

            // SHOW only: CREATE..REORDER would marshal every object event in the session onto the UI thread
            trayObjectEventHook = SetWinEventHook(
                EVENT_OBJECT_SHOW,
                EVENT_OBJECT_SHOW,
                IntPtr.Zero,
                trayEventProc,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            trayForegroundEventHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                trayEventProc,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        private void UnhookTrayEvents()
        {
            if (trayObjectEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(trayObjectEventHook);
                trayObjectEventHook = IntPtr.Zero;
            }

            if (trayForegroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(trayForegroundEventHook);
                trayForegroundEventHook = IntPtr.Zero;
            }

            trayEventProc = null;
        }

        private void TrayWinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (HwndTray == IntPtr.Zero) return;

                if (eventType != EVENT_SYSTEM_FOREGROUND &&
                    eventType != EVENT_OBJECT_SHOW)
                {
                    return;
                }

                // cheap filter before GetClassName: whole top-level windows only, never our own
                if (hWnd == IntPtr.Zero || hWnd == HwndTray || idObject != 0 || idChild != 0 ||
                    GetParent(hWnd) != IntPtr.Zero)
                {
                    return;
                }

                StringBuilder className = new StringBuilder(32);
                GetClassName(hWnd, className, className.Capacity);
                if (className.ToString() == TrayWndClass)
                {
                    ScheduleTrayCheck();
                }
            }
            catch (Exception ex)
            {
                // UltraWinBar: this runs on a native call frame; nothing may throw back into it.
                ShellLogger.Error("TrayService: Error handling tray WinEvent", ex);
            }
        }

        // UltraWinBar: coalesce bursts of WinEvents (and our own WM_WINDOWPOSCHANGED) into a
        // single deferred check instead of running MakeTrayTopmost inline per event.
        private void ScheduleTrayCheck()
        {
            if (trayCheckQueued) return;
            trayCheckQueued = true;

            trayMonitor.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                trayCheckQueued = false;
                CheckTrayZOrder();
            }));
        }

        private void SetWindowsTrayBottommost()
        {
            IntPtr taskbarHwnd = WindowHelper.FindWindowsTray(HwndTray);

            if (taskbarHwnd != IntPtr.Zero)
            {
                SetWindowPos(taskbarHwnd, (IntPtr)WindowZOrder.HWND_BOTTOM, 0, 0, 0, 0,
                    (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOSIZE |
                    (int)SetWindowPosFlags.SWP_NOACTIVATE);
            }
        }

        private void MakeTrayTopmost()
        {
            if (HwndTray != IntPtr.Zero)
            {
                SetWindowPos(HwndTray, (IntPtr)WindowZOrder.HWND_TOPMOST, 0, 0, 0, 0,
                    (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOACTIVATE |
                    (int)SetWindowPosFlags.SWP_NOSIZE);
            }
        }
        #endregion
    }
}