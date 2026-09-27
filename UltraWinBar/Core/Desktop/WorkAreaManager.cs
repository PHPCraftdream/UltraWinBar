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
            return TryGetCurrent(out var current) &&
                current.Left == expected.Left && current.Top == expected.Top &&
                current.Right == expected.Right && current.Bottom == expected.Bottom;
        }

        public static bool TryGetCurrent(out NativeMethods.Rect current)
        {
            current = new NativeMethods.Rect();
            return NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.GETWORKAREA, 0, ref current, 0);
        }

        public static void Apply(NativeMethods.Rect workArea, uint UltraWinBarProcessId)
        {
            if (IsCurrent(workArea)) return;
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
                if (EnumWindows((hWnd, lParam) =>
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

    internal enum WorkAreaRecoveryResult { Unchanged, Deferred, Applied, Suspended }

    internal sealed class WorkAreaRecovery
    {
        private const long Cooldown = 2000, QuietPeriod = 60000;
        private const int BurstLimit = 3;
        private static readonly long[] Backoff = { 60000, 300000, 900000 };
        private bool recovering, attempted, suspended;
        private long lastAttempt, quietSince, suspendedUntil;
        private int attempts, suspensions;

        internal long RetryAt { get; private set; }
        internal long SuspendedFor { get; private set; }

        internal WorkAreaRecoveryResult Recover(long now, Func<bool> isCurrent, Action apply)
        {
            if (recovering)
            {
                RetryAt = now + Cooldown;
                return WorkAreaRecoveryResult.Deferred;
            }
            if (suspended)
            {
                if (now < suspendedUntil)
                {
                    RetryAt = suspendedUntil;
                    return WorkAreaRecoveryResult.Deferred;
                }
                // One probe write after the pause; a renewed conflict suspends for longer.
                suspended = false;
                quietSince = suspendedUntil;
                attempts = BurstLimit - 1;
            }
            recovering = true;
            try
            {
                if (isCurrent()) return WorkAreaRecoveryResult.Unchanged;
                if (attempted && now - lastAttempt < Cooldown)
                {
                    RetryAt = lastAttempt + Cooldown;
                    return WorkAreaRecoveryResult.Deferred;
                }
                if (now - quietSince >= QuietPeriod)
                {
                    attempts = 0;
                    suspensions = 0;
                }
                if (attempts >= BurstLimit)
                {
                    SuspendedFor = Backoff[Math.Min(suspensions++, Backoff.Length - 1)];
                    suspended = true;
                    suspendedUntil = now + SuspendedFor;
                    RetryAt = suspendedUntil;
                    return WorkAreaRecoveryResult.Suspended;
                }

                attempted = true;
                lastAttempt = quietSince = now;
                attempts++;
                apply();
                return WorkAreaRecoveryResult.Applied;
            }
            finally { recovering = false; }
        }

        internal void Reset()
        {
            suspended = attempted = false;
            attempts = suspensions = 0;
        }
    }
}
