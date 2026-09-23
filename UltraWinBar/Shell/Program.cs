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

            if (!SingleInstanceCheck())
            {
                return 1;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            App app = new App();
            app.InitializeComponent();

            return app.Run();
        }

        internal static void StartWorkAreaWatchdog(NativeMethods.Rect workArea)
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
            Process.Start(startInfo);
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

            NativeMethods.Rect workArea = new NativeMethods.Rect
            {
                Left = int.Parse(args[2], CultureInfo.InvariantCulture),
                Top = int.Parse(args[3], CultureInfo.InvariantCulture),
                Right = int.Parse(args[4], CultureInfo.InvariantCulture),
                Bottom = int.Parse(args[5], CultureInfo.InvariantCulture)
            };
            WorkAreaManager.Apply(workArea, (uint)Process.GetCurrentProcess().Id);
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
