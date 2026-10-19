using System;
using System.Threading;

namespace ManagedShell.Common.Native
{
    // UltraWinBar (K12): ShellComProxy needs to know when explorer.exe (and every COM object it
    // hosts) has restarted, but this vendor code must not reference the app's ExplorerMonitor. The
    // app raises this after handling TaskbarCreated; ShellComProxy subscribes here instead.
    internal static class ExplorerLifecycle
    {
        internal static event EventHandler Restarted;

        // UltraWinBar: our own TaskbarCreated broadcast (announcing the tray) reaches our windows inside the call.
        // It is not Explorer restarting, so ExplorerMonitor must not rebuild the panels for it.
        private static int selfAnnouncing;

        internal static bool IsSelfAnnouncing => Volatile.Read(ref selfAnnouncing) > 0;

        internal static IDisposable BeginSelfAnnouncement()
        {
            Interlocked.Increment(ref selfAnnouncing);
            return new SelfAnnouncement();
        }

        private sealed class SelfAnnouncement : IDisposable
        {
            private int ended;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref ended, 1) == 0)
                {
                    Interlocked.Decrement(ref selfAnnouncing);
                }
            }
        }

        // One failing subscriber must not skip the rest.
        internal static void RaiseRestarted()
        {
            var handlers = Restarted;
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((EventHandler)handler)(null, EventArgs.Empty); }
                catch (Exception error) { Logging.ShellLogger.Warning($"ExplorerLifecycle.Restarted handler failed: {error.Message}"); }
            }
        }
    }
}
