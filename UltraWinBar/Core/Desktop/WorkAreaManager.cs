using ManagedShell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace UltraWinBar.Utilities
{
    internal static class WorkAreaManager
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private static readonly object NotificationLock = new object();
        private static bool notificationPending;
        private static bool notificationWorkerRunning;
        private static uint notificationProcessId;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam,
            IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        public static bool IsCurrent(NativeMethods.Rect expected)
        {
            var current = new NativeMethods.Rect();
            return NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.GETWORKAREA, 0, ref current, 0) &&
                current.Left == expected.Left && current.Top == expected.Top &&
                current.Right == expected.Right && current.Bottom == expected.Bottom;
        }

        public static void Apply(NativeMethods.Rect workArea, uint UltraWinBarProcessId)
        {
            if (!NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.SETWORKAREA, 0, ref workArea, 0))
            {
                ManagedShell.Common.Logging.ShellLogger.Error("WorkAreaManager: Failed to set the work area.");
                return;
            }

            lock (NotificationLock)
            {
                notificationProcessId = UltraWinBarProcessId;
                notificationPending = true;
                if (notificationWorkerRunning)
                {
                    return;
                }

                notificationWorkerRunning = true;
            }

            Task.Run(DispatchPendingNotifications);
        }

        private static void DispatchPendingNotifications()
        {
            try
            {
                while (true)
                {
                    uint processId;
                    lock (NotificationLock)
                    {
                        if (!notificationPending)
                        {
                            return;
                        }

                        notificationPending = false;
                        processId = notificationProcessId;
                    }

                    BroadcastSettingChange(processId);
                }
            }
            catch (Exception ex)
            {
                try { ManagedShell.Common.Logging.ShellLogger.Error($"WorkAreaManager: Notification failed: {ex.Message}"); }
                catch { }
            }
            finally
            {
                bool restartWorker;
                lock (NotificationLock)
                {
                    notificationWorkerRunning = notificationPending;
                    restartWorker = notificationWorkerRunning;
                }

                if (restartWorker)
                {
                    Task.Run(DispatchPendingNotifications);
                }
            }
        }

        private static void BroadcastSettingChange(uint UltraWinBarProcessId)
        {
            IntPtr settingName = Marshal.StringToHGlobalUni("WorkArea");
            try
            {
                if (!EnumWindows((hWnd, lParam) =>
                {
                    GetWindowThreadProcessId(hWnd, out uint processId);
                    if (processId == UltraWinBarProcessId)
                    {
                        return true;
                    }

                    if (!IsWindowVisible(hWnd))
                    {
                        return true;
                    }

                    StringBuilder className = new StringBuilder(64);
                    GetClassName(hWnd, className, className.Capacity);
                    if (className.ToString() == "Shell_TrayWnd" || className.ToString() == "Shell_SecondaryTrayWnd")
                    {
                        return true;
                    }

                    SendMessageTimeout(hWnd, (uint)NativeMethods.WM.SETTINGCHANGE,
                        (IntPtr)NativeMethods.SPI.SETWORKAREA, settingName, 0x2, 250, out _);
                    return true;
                }, IntPtr.Zero))
                {
                    return;
                }

                ManagedShell.Common.Logging.ShellLogger.Warning("WorkAreaManager: Could not enumerate top-level windows for notification.");
            }
            finally
            {
                Marshal.FreeHGlobal(settingName);
            }
        }
    }
}
