using ManagedShell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    internal static class WorkAreaManager
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private static readonly object NotificationLock = new object();
        private static bool notificationPending;
        private static bool notificationWorkerRunning;
        private static uint notificationProcessId;
        private static Task pendingNotificationTask = Task.CompletedTask;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

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
                pendingNotificationTask = Task.Run(DispatchPendingNotifications);
            }
        }

        /// Blocks the caller (watchdog exit / app shutdown) until the settings-change
        /// broadcast started by the most recent Apply finishes, bounded by timeout.
        /// Normal runtime callers never call this; the broadcast itself stays async there.
        /// Loops because the worker may chain into a fresh Task if Apply raced it; the
        /// overall wait still never exceeds timeout.
        public static bool WaitForPendingNotifications(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                Task task;
                lock (NotificationLock)
                {
                    task = pendingNotificationTask;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return task.IsCompleted;
                }

                try
                {
                    if (!task.Wait(remaining))
                    {
                        return false;
                    }
                }
                catch (AggregateException)
                {
                    // DispatchPendingNotifications already logs its own failures.
                }

                lock (NotificationLock)
                {
                    if (!notificationWorkerRunning || pendingNotificationTask == task)
                    {
                        return true;
                    }
                }
            }
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
                lock (NotificationLock)
                {
                    notificationWorkerRunning = notificationPending;
                    if (notificationWorkerRunning)
                    {
                        pendingNotificationTask = Task.Run(DispatchPendingNotifications);
                    }
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

    // Manual layout panels are also listed as shell AppBars with their exact rects, so Explorer's own
    // work-area recomputation (on unlock, display or AppBar changes) reserves them instead of clearing it.
    internal static class PanelReservation
    {
        internal const int AbnPosChanged = 1;
        internal static readonly int CallbackMessage = RegisterWindowMessage("UltraWinBarPanelReservation");

        internal static APPBARDATA Build(IntPtr hwnd, ManagedShell.AppBar.AppBarEdge edge, NativeMethods.Rect rect) => new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = CallbackMessage,
            uEdge = (int)edge,
            rc = rect
        };

        /// Registers on first call (registered == false), then (re)publishes the rect. Returns the registration state.
        internal static bool Reserve(IntPtr hwnd, ManagedShell.AppBar.AppBarEdge edge, NativeMethods.Rect rect, bool registered)
        {
            var data = Build(hwnd, edge, rect);
            if (!registered && SHAppBarMessage((int)ABMsg.ABM_NEW, ref data) == 0)
            {
                ManagedShell.Common.Logging.ShellLogger.Warning($"PanelReservation: Could not register {edge} panel with the shell.");
                return false;
            }
            SHAppBarMessage((int)ABMsg.ABM_SETPOS, ref data);
            return true;
        }

        internal static void Release(IntPtr hwnd)
        {
            var data = Build(hwnd, default, default);
            SHAppBarMessage((int)ABMsg.ABM_REMOVE, ref data);
        }
    }
}
