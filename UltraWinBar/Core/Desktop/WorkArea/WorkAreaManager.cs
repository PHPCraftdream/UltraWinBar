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

    /// <summary>
    /// The work area of the last layout and the screen it was laid out for, as text. The next start puts it back at once,
    /// when the screen is the same: the windows keep their place while the panels start, instead of growing over the strips
    /// the panels will take and shrinking again.
    /// </summary>
    internal static class WorkAreaMemory
    {
        internal static string Format(NativeMethods.Rect area, NativeMethods.Rect screen) =>
            $"{area.Left},{area.Top},{area.Right},{area.Bottom};{screen.Left},{screen.Top},{screen.Right},{screen.Bottom}";

        /// <summary>False for anything but a non-empty work area that lies within the screen.</summary>
        internal static bool TryParse(string text, out NativeMethods.Rect area, out NativeMethods.Rect screen)
        {
            area = default;
            screen = default;
            string[] parts = (text ?? "").Split(';');
            if (parts.Length != 2 || !TryRect(parts[0], out area) || !TryRect(parts[1], out screen)) return false;
            return area.Width > 0 && area.Height > 0 && screen.Width > 0 && screen.Height > 0 &&
                area.Left >= screen.Left && area.Top >= screen.Top && area.Right <= screen.Right && area.Bottom <= screen.Bottom;
        }

        /// <summary>
        /// The original work area (the one before the panels took their strips) when the screen still holds the panels' own,
        /// left there by a restart that kept it: the live value then is the panels' layout, not the original. Otherwise the
        /// live value is the original.
        /// </summary>
        internal static NativeMethods.Rect ResolveOriginal(NativeMethods.Rect current, NativeMethods.Rect screen, string last, string original)
        {
            if (TryParse(last, out var lastArea, out var lastScreen) && lastScreen.Equals(screen) && lastArea.Equals(current) &&
                TryParse(original, out var originalArea, out var originalScreen) && originalScreen.Equals(screen) && !originalArea.Equals(current))
            {
                return originalArea;
            }
            return current;
        }

        private static bool TryRect(string text, out NativeMethods.Rect rect)
        {
            rect = default;
            string[] numbers = text.Split(',');
            if (numbers.Length != 4) return false;
            var values = new int[4];
            for (int i = 0; i < 4; i++)
                if (!int.TryParse(numbers[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out values[i])) return false;
            rect = new NativeMethods.Rect { Left = values[0], Top = values[1], Right = values[2], Bottom = values[3] };
            return true;
        }
    }

    /// <summary>
    /// A file that says the program is being restarted on purpose (the Restart menu, the deploy script): the work area is then
    /// left as it is, instead of being given back, so that the windows keep their limits while the panels are away, and the
    /// next start takes it over. A stale file (older than two minutes) means nothing, so a crash is never mistaken for it.
    /// </summary>
    internal static class RestartMarker
    {
        private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(2);

        /// <summary>For the tests: another place for the file.</summary>
        internal static string PathOverride { get; set; }

        internal static string FilePath => PathOverride ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltraWinBar", "restarting");

        internal static void Create()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                System.IO.File.WriteAllText(FilePath, DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
            {
                ManagedShell.Common.Logging.ShellLogger.Debug($"RestartMarker: could not write: {error.Message}");
            }
        }

        internal static bool IsFresh()
        {
            try
            {
                return System.IO.File.Exists(FilePath) && DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(FilePath) < Freshness;
            }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static void Clear()
        {
            try { System.IO.File.Delete(FilePath); }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException) { }
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

        /// Republishing an unchanged rect costs a shell broadcast for nothing, unless the shell asked for it.
        internal static bool ShouldPublish(bool registered, NativeMethods.Rect? published, NativeMethods.Rect rect, bool force) =>
            !registered || force || !published.HasValue || !published.Value.Equals(rect);

        internal static void Release(IntPtr hwnd)
        {
            var data = Build(hwnd, default, default);
            SHAppBarMessage((int)ABMsg.ABM_REMOVE, ref data);
        }
    }
}
