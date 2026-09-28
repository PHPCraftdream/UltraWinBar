using System;

namespace ManagedShell.Common.Native
{
    // UltraWinBar (K12): ShellComProxy needs to know when explorer.exe (and every COM object it
    // hosts) has restarted, but this vendor code must not reference the app's ExplorerMonitor. The
    // app raises this after handling TaskbarCreated; ShellComProxy subscribes here instead.
    internal static class ExplorerLifecycle
    {
        internal static event EventHandler Restarted;

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
