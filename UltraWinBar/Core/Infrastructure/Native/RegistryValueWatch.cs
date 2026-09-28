using ManagedShell.Common.Helpers;
using ManagedShell.Common.Native;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    // R9-L (K18): one RegNotifyChangeKeyValue watcher, replacing the two copies that had drifted
    // apart (VirtualDesktopContext.RegistryWatch applied REG_NOTIFY_THREAD_AGNOSTIC unconditionally;
    // JapaneseIme's KanaModeWatch checked the Windows version but was a hand-copied duplicate).
    // Auto re-arms before notifying (a change during dispatch must not be missed), marshals the
    // notification to the owner's dispatcher, and never leaks the wait registration or key handle.
    internal sealed class RegistryValueWatch : IDisposable
    {
        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(IntPtr key, bool watchSubtree, uint filter, IntPtr signal, bool asynchronous);

        internal const uint ValueChangeFilter = 0x00000004; // REG_NOTIFY_CHANGE_LAST_SET
        internal const uint NameChangeFilter = 0x00000001; // REG_NOTIFY_CHANGE_NAME
        // Undocumented before Windows 8; RegNotifyChangeKeyValue can fail with it on Windows 7, so it
        // is added here rather than baked into the filter constants above.
        private const uint ThreadAgnosticFilter = 0x10000000; // REG_NOTIFY_THREAD_AGNOSTIC

        private readonly RegistryKey key;
        private readonly uint filter;
        private readonly Dispatcher dispatcher;
        private readonly Action changed;
        private readonly string source;
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        private readonly RegisteredWaitHandle wait;
        private readonly object gate = new object();
        private bool disposed;

        // Takes ownership of "key": disposed together with the watch. "changed" runs on "dispatcher",
        // never on the wait callback's thread pool thread. "source" identifies this watch to CallbackGuard.
        internal RegistryValueWatch(RegistryKey key, uint filter, Dispatcher dispatcher, Action changed, string source)
        {
            this.key = key;
            this.filter = filter;
            this.dispatcher = dispatcher;
            this.changed = changed;
            this.source = source;
            wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, __) =>
            {
                try
                {
                    lock (gate)
                    {
                        if (disposed) return;
                        Arm();
                        dispatcher.BeginInvoke(changed);
                    }
                }
                catch (Exception error) { CallbackGuard.Report(source, error); }
            }, null, Timeout.Infinite, false);
            Arm();
        }

        private void Arm()
        {
            uint effectiveFilter = filter;
            if (EnvironmentHelper.IsWindows8OrBetter) effectiveFilter |= ThreadAgnosticFilter;
            RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), false, effectiveFilter,
                signal.SafeWaitHandle.DangerousGetHandle(), true);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                wait.Unregister(null);
                key.Dispose();
                signal.Dispose();
            }
        }
    }
}
