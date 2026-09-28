using ManagedShell.Common.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace ManagedShell.Common.Native
{
    // UltraWinBar (K12): moved from the app so ManagedShell's own native callbacks (tray WNDPROC,
    // WinEvent hooks, EnumWindows) share the same barrier. An exception escaping code entered from
    // native (WinEvent/hook/COM/thread-pool callbacks) terminates the process past
    // DispatcherUnhandledException; log it instead, rate-limited.
    internal static class CallbackGuard
    {
        private const int LoggedPerSource = 20;
        private static readonly ConcurrentDictionary<string, int> failures = new ConcurrentDictionary<string, int>();

        internal static void Report(string source, Exception error)
        {
            try
            {
                int count = failures.AddOrUpdate(source, 1, (_, previous) => previous + 1);
                if (count <= LoggedPerSource) ShellLogger.Error($"{source}: callback failed ({count}): {error}");
                else if (count == LoggedPerSource + 1) ShellLogger.Error($"{source}: further callback failures are not logged.");
            }
            catch (Exception) { }
        }

        internal static int FailureCount(string source) => failures.TryGetValue(source, out int count) ? count : 0;

        // Total across all sources, for the health line (K19).
        internal static int TotalFailureCount => failures.Values.Sum();

        // Last-chance logging: background-thread exceptions otherwise leave no trace in our log.
        internal static void InstallGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try { ShellLogger.Error($"Unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}"); }
                catch (Exception) { }
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Report("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }
    }
}
