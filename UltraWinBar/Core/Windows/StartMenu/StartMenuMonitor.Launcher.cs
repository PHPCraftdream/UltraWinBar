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
    public partial class StartMenuMonitor
    {
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

        private void InvalidateCachedLaunchers()
        {
            _cachedLauncherRS1 = null;
            _cachedLauncherWin81 = null;
            _cachedLauncherMonitor = IntPtr.Zero;
        }

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
            _taskbarHwndActivated = taskbarHwnd;
            _placementTaskbar = taskbarHwnd;
            UpdateMenuEventHook();
            if (TryOpenShellDirectInvoke()) return;

            if (!EnvironmentHelper.IsWindows10OrBetter ||
                FindWindowEx(IntPtr.Zero, IntPtr.Zero, "OpenShell.COwnerWindow", IntPtr.Zero) != IntPtr.Zero ||
                FindWindowEx(IntPtr.Zero, IntPtr.Zero, "DV2ControlHost", IntPtr.Zero) != IntPtr.Zero)
            {
                // Invoke once, without a delayed second request that can toggle the menu closed.
                ShellLogger.Debug("StartMenuMonitor: falling back to ShellHelper.ShowStartMenu (SendInput) — OpenShell/DV2ControlHost detected or pre-Win10");
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
                    if (immersiveLauncher != null)
                    {
                        int hr = immersiveLauncher.ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD.ILSM_STARTBUTTON, IMMERSIVELAUNCHERSHOWFLAGS.ILSF_IGNORE_SET_FOREGROUND_ERROR);
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
                // PreserveSig: a severed cached launcher now fails with an HRESULT instead of throwing.
                InvalidateCachedLaunchers();
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"StartMenuMonitor: Failed to show Start menu via IImmersiveLauncher: {e}");

                // The cached launcher may be a stale RCW (e.g. explorer.exe restarted);
                // drop it so the next press re-queries a fresh one instead of failing forever.
                InvalidateCachedLaunchers();
            }

            ShellHelper.ShowStartMenu();
        }
    }
}
