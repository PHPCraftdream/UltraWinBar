using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using ManagedShell.Interop;
using UltraWinBar.Utilities;

namespace UltraWinBar
{
    internal sealed class Program
    {
        private const string MutexName = "UltraWinBar";
        private const int MutexAttempts = 10;
        private const int MutexWaitMs = 1000;
        private static readonly TimeSpan WorkAreaBroadcastTimeout = TimeSpan.FromSeconds(3);

        private static System.Threading.Mutex _UltraWinBarMutex;

        /// <summary>
        /// The main entry point for the application
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 6 && args[0] == "--restore-work-area")
            {
                return RunWorkAreaWatchdog(args);
            }

            StartupTimeline.Mark("Main entered");

            // Before waiting for the last instance to exit: the Restart menu starts this one while that one still lives.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            StartReservationBridge();

            if (!SingleInstanceCheck())
            {
                return 1;
            }

            // SystemEvents starts its own thread on first use and waits for it (1.2 s at start); Screen.AllScreens
            // is that first use, on the UI thread. Start it here, off the UI thread.
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Microsoft.Win32.SystemEvents.InvokeOnEventsThread(new Action(() => { })); }
                catch (Exception e) { ManagedShell.Common.Logging.ShellLogger.Debug($"Program: SystemEvents warm-up failed: {e.Message}"); }
            });

            App app = new App();
            app.InitializeComponent();

            return app.Run();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        // A restart on purpose: the strips of the last run are held until the new panels have published theirs.
        private static void StartReservationBridge()
        {
            try
            {
                const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_CMONITORS = 80;
                if (!RestartMarker.IsFresh() || Settings.Instance.AutoHide || GetSystemMetrics(SM_CMONITORS) != 1 ||
                    !WorkAreaMemory.TryParse(Settings.Instance.LastWorkArea, out var area, out var screen) ||
                    !screen.Equals(new NativeMethods.Rect(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN))))
                {
                    return;
                }
                ReservationBridge.Start(area, screen);
            }
            catch (Exception e) { ManagedShell.Common.Logging.ShellLogger.Debug($"Program: reservation bridge not started: {e.Message}"); }
        }

        internal static Process StartWorkAreaWatchdog(NativeMethods.Rect workArea)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--restore-work-area");
            startInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(workArea.Left.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(workArea.Top.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(workArea.Right.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(workArea.Bottom.ToString(CultureInfo.InvariantCulture));
            return Process.Start(startInfo);
        }

        /// Swaps a stale watchdog for one carrying the current work area. Kills the old
        /// child by our own handle (never by image name) before starting the replacement,
        /// so a burst of display-change events can't leave orphans behind.
        internal static Process RestartWorkAreaWatchdog(Process previous, NativeMethods.Rect workArea)
        {
            StopWorkAreaWatchdog(previous);
            return StartWorkAreaWatchdog(workArea);
        }

        private static void StopWorkAreaWatchdog(Process watchdog)
        {
            if (watchdog == null)
            {
                return;
            }

            try
            {
                if (!watchdog.HasExited)
                {
                    watchdog.Kill();
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally
            {
                watchdog.Dispose();
            }
        }

        private static int RunWorkAreaWatchdog(string[] args)
        {
            try
            {
                int parentPid = int.Parse(args[1], CultureInfo.InvariantCulture);
                using (Process parent = Process.GetProcessById(parentPid))
                {
                    parent.WaitForExit();
                }
            }
            catch (ArgumentException)
            {
            }

            // A restart on purpose keeps the work area, so that the windows keep their limits while the panels are away; the
            // next start takes it over. A crash leaves no such mark, and the original work area is given back at once.
            if (RestartMarker.IsFresh())
            {
                return 0;
            }

            NativeMethods.Rect workArea = new NativeMethods.Rect
            {
                Left = int.Parse(args[2], CultureInfo.InvariantCulture),
                Top = int.Parse(args[3], CultureInfo.InvariantCulture),
                Right = int.Parse(args[4], CultureInfo.InvariantCulture),
                Bottom = int.Parse(args[5], CultureInfo.InvariantCulture)
            };
            WorkAreaManager.Apply(workArea, (uint)Process.GetCurrentProcess().Id);
            // Wait for the broadcast so the process doesn't exit before other apps see the restored work area.
            WorkAreaManager.WaitForPendingNotifications(WorkAreaBroadcastTimeout);
            return 0;
        }

        private static bool GetMutex()
        {
            _UltraWinBarMutex = new System.Threading.Mutex(true, MutexName, out bool ok);

            return ok;
        }

        private static bool SingleInstanceCheck()
        {
            for (int i = 0; i < MutexAttempts; i++)
            {
                if (!GetMutex())
                {
                    // Dispose the mutex, otherwise it will never create new
                    _UltraWinBarMutex.Dispose();
                    System.Threading.Thread.Sleep(MutexWaitMs);
                }
                else
                {
                    return true;
                }
            }

            return false;
        }
    }
}
