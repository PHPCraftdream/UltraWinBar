using ManagedShell.Common.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    internal sealed class VirtualDesktopContext : IDisposable
    {
        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] out bool current);
            [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktop);
            [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktop);
        }
        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(IntPtr key, bool subtree, uint filter, IntPtr signal, bool asynchronous);
        private const uint ValueChangeFilter = 0x10000004; // REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC
        private const uint NameChangeFilter = 0x10000001; // REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_THREAD_AGNOSTIC
        private const string GlobalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
        private static readonly int SessionId = ReadSessionId();
        private static readonly string SessionPath = BuildSessionPath(SessionId);
        private static readonly Guid ManagerClsid = new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A");
        // HRESULTs seen when explorer.exe (host of the manager) restarts and the RCW's proxy is severed.
        private const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
        private const int RPC_S_SERVER_UNAVAILABLE = unchecked((int)0x800706BA);
        private const int CO_E_OBJNOTCONNECTED = unchecked((int)0x800401FD);
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly List<RegistryTreeWatch> watches = new List<RegistryTreeWatch>();
        private readonly object managerGate = new object();
        private IDesktopManager manager;
        // Null manager = needs (re)creation on use, with backoff.
        private DateTime nextRetryUtc = DateTime.MinValue;
        private TimeSpan retryDelay = InitialRetryDelay;
        private bool retryFailureLogged;
        private bool disposed;
        public static VirtualDesktopContext Instance { get; private set; }
        public Guid CurrentId { get; private set; }
        public event EventHandler Changed;

        private static int ReadSessionId()
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }

        internal static string BuildSessionPath(int sessionId) =>
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\" + sessionId + @"\VirtualDesktops";

        public VirtualDesktopContext()
        {
            Instance = this;
            manager = CreateManager();
            CurrentId = ReadCurrent();
            foreach (string path in new[] { GlobalPath, SessionPath })
                watches.Add(new RegistryTreeWatch(path, dispatcher, () => dispatcher.BeginInvoke(new Action(Refresh))));
            ExplorerMonitor.ExplorerRestarted += ExplorerMonitor_ExplorerRestarted;
            ShellLogger.Info($"Virtual desktop: {CurrentId}");
        }

        private Guid ReadCurrent()
        {
            foreach (string path in new[] { SessionPath, GlobalPath })
            {
                using var key = Registry.CurrentUser.OpenSubKey(path);
                if (key?.GetValue("CurrentVirtualDesktop") is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
            }
            using var global = Registry.CurrentUser.OpenSubKey(GlobalPath);
            if (global?.GetValue("VirtualDesktopIDs") is byte[] ids && ids.Length == 16) return new Guid(ids);
            return CurrentId;
        }

        private void Refresh()
        {
            if (disposed) return;
            RefreshCurrent();
        }

        public Guid CurrentIdSnapshot() => ReadCurrent();

        // Re-reads the current desktop and raises Changed if it moved; dispatcher thread only.
        internal bool RefreshCurrent()
        {
            Guid id = ReadCurrent();
            if (id == CurrentId) return false;
            CurrentId = id;
            ShellLogger.Debug($"Virtual desktop changed: {id}");
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static IDesktopManager CreateManager()
        {
            try { return (IDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClsid)); }
            catch (COMException) { return null; }
        }

        // Explorer restarts sever the proxy: PreserveSig calls return these HRESULTs, others throw.
        private static bool IsDisconnected(int hr) =>
            hr == RPC_E_DISCONNECTED || hr == RPC_S_SERVER_UNAVAILABLE || hr == CO_E_OBJNOTCONNECTED;

        private static bool IsDisconnected(Exception error) =>
            error is InvalidComObjectException || (error is COMException && IsDisconnected(error.HResult));

        private int Checked(int hr)
        {
            if (IsDisconnected(hr)) MarkStale($"HRESULT 0x{hr:X8}");
            return hr;
        }

        private void OnComError(Exception error)
        {
            if (IsDisconnected(error)) MarkStale(error.Message);
        }

        // Explorer registers the CLSID at runtime, possibly after TaskbarCreated: retry from scratch.
        private void ExplorerMonitor_ExplorerRestarted(object sender, EventArgs e)
        {
            MarkStale("explorer restarted");
            lock (managerGate)
            {
                nextRetryUtc = DateTime.MinValue;
                retryDelay = InitialRetryDelay;
            }
            EnsureManager();
        }

        // Drops a severed proxy; recreation keeps backing off so a half-started shell can't cause a storm.
        private void MarkStale(string reason)
        {
            IDesktopManager stale;
            lock (managerGate)
            {
                stale = manager;
                if (stale == null) return;
                manager = null;
                nextRetryUtc = DateTime.UtcNow + retryDelay;
                retryDelay = NextRetryDelay(retryDelay);
            }
            try { Marshal.ReleaseComObject(stale); } catch (Exception) { }
            ShellLogger.Warning($"Virtual desktop manager disconnected ({reason}); will recreate on next use.");
        }

        private static TimeSpan NextRetryDelay(TimeSpan current)
        {
            if (current < TimeSpan.FromSeconds(5)) return TimeSpan.FromSeconds(5);
            if (current < TimeSpan.FromSeconds(15)) return TimeSpan.FromSeconds(15);
            return TimeSpan.FromSeconds(60);
        }

        // Lazy (re)creation with backoff 2/5/15/60 s; failures logged once per streak.
        private IDesktopManager EnsureManager()
        {
            lock (managerGate)
            {
                if (disposed) return null;
                if (manager != null) return manager;
                DateTime now = DateTime.UtcNow;
                if (now < nextRetryUtc) return null;
                var created = CreateManager();
                if (created != null)
                {
                    manager = created;
                    if (retryFailureLogged)
                    {
                        ShellLogger.Info("Virtual desktop manager recreated.");
                        retryFailureLogged = false;
                    }
                    return manager;
                }
                if (!retryFailureLogged)
                {
                    ShellLogger.Warning("Virtual desktop manager unavailable; will keep retrying.");
                    retryFailureLogged = true;
                }
                nextRetryUtc = now + retryDelay;
                retryDelay = NextRetryDelay(retryDelay);
                return null;
            }
        }

        public bool TryGetWindowDesktopId(IntPtr hwnd, out Guid id)
        {
            id = Guid.Empty;
            var current = EnsureManager();
            try { return current != null && Checked(current.GetWindowDesktopId(hwnd, out id)) >= 0 && id != Guid.Empty; }
            catch (Exception error) when (error is COMException || error is InvalidComObjectException)
            {
                OnComError(error);
                return false;
            }
        }

        public Guid DesktopForWindow(IntPtr hwnd)
        {
            if (TryGetWindowDesktopId(hwnd, out Guid id)) return id;
            return CurrentId;
        }

        public bool TryMoveWindowToDesktop(IntPtr hwnd, Guid destination)
        {
            if (hwnd == IntPtr.Zero || destination == Guid.Empty) return false;
            var current = EnsureManager();
            try
            {
                if (current != null && Checked(current.MoveWindowToDesktop(hwnd, ref destination)) >= 0) return true;
            }
            catch (Exception error) when (error is COMException || error is InvalidComObjectException)
            {
                ShellLogger.Warning($"Desktop move COM failed for {hwnd}: {error.Message}");
                OnComError(error);
            }

            if (!DesktopActions.IsSupported) return false;
            try
            {
                using var actions = new DesktopActions();
                actions.MoveWindow(hwnd, destination);
                return true;
            }
            catch (Exception error)
            {
                ShellLogger.Warning($"Desktop move failed for {hwnd}: {error.Message}");
                return false;
            }
        }

        public bool IsOnCurrentDesktop(IntPtr hwnd)
        {
            var current = EnsureManager();
            try { return current == null || Checked(current.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrent)) < 0 || onCurrent; }
            catch (Exception error) when (error is COMException || error is InvalidComObjectException)
            {
                OnComError(error);
                return true;
            }
        }

        public void Dispose()
        {
            disposed = true;
            ExplorerMonitor.ExplorerRestarted -= ExplorerMonitor_ExplorerRestarted;
            foreach (var watch in watches) watch.Dispose();
            lock (managerGate)
            {
                if (manager != null) Marshal.ReleaseComObject(manager);
                manager = null;
            }
            Instance = null;
        }

        private sealed class RegistryWatch : IDisposable
        {
            private readonly RegistryKey key;
            private readonly uint filter;
            private readonly AutoResetEvent signal = new AutoResetEvent(false);
            private readonly RegisteredWaitHandle wait;
            private readonly object gate = new object();
            private bool disposed;
            public RegistryWatch(RegistryKey key, uint filter, Action changed)
            {
                this.key = key;
                this.filter = filter;
                wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, __) =>
                {
                    lock (gate)
                    {
                        if (disposed) return;
                        Arm();
                        changed();
                    }
                }, null, Timeout.Infinite, false);
                Arm();
            }
            private void Arm() => RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), false, filter,
                signal.SafeWaitHandle.DangerousGetHandle(), true);
            public void Dispose()
            {
                lock (gate)
                {
                    disposed = true;
                    wait.Unregister(null);
                    key.Dispose();
                    signal.Dispose();
                }
            }
        }

        // Watches a registry leaf Explorer creates lazily and can delete/recreate (e.g. on its
        // own restart). Besides the leaf's value, it watches the nearest existing ancestor for
        // name changes, so a missing-at-start or deleted-and-recreated leaf is picked back up
        // instead of leaving the watch permanently deaf for the rest of the process uptime.
        // Tradeoff: only ancestor name-changes trigger a rescan (cheap, no polling), but a
        // rescan re-opens both watches even for unrelated siblings changing under that ancestor.
        private sealed class RegistryTreeWatch : IDisposable
        {
            private readonly string leafPath;
            private readonly Dispatcher dispatcher;
            private readonly Action changed;
            private readonly object gate = new object();
            private RegistryWatch leafWatch;
            private RegistryWatch ancestorWatch;
            private bool disposed;

            public RegistryTreeWatch(string leafPath, Dispatcher dispatcher, Action changed)
            {
                this.leafPath = leafPath;
                this.dispatcher = dispatcher;
                this.changed = changed;
                Rebuild();
            }

            internal static string ParentPath(string path)
            {
                int index = path.LastIndexOf('\\');
                return index > 0 ? path.Substring(0, index) : null;
            }

            internal static IEnumerable<string> AncestorChainFrom(string path)
            {
                for (string ancestor = ParentPath(path); ancestor != null; ancestor = ParentPath(ancestor))
                    yield return ancestor;
            }

            private void Rebuild()
            {
                lock (gate)
                {
                    if (disposed) return;
                    leafWatch?.Dispose();
                    leafWatch = null;
                    ancestorWatch?.Dispose();
                    ancestorWatch = null;

                    var leafKey = Registry.CurrentUser.OpenSubKey(leafPath);
                    if (leafKey != null) leafWatch = new RegistryWatch(leafKey, ValueChangeFilter, changed);

                    foreach (string ancestorPath in AncestorChainFrom(leafPath))
                    {
                        var ancestorKey = Registry.CurrentUser.OpenSubKey(ancestorPath);
                        if (ancestorKey == null) continue;
                        ancestorWatch = new RegistryWatch(ancestorKey, NameChangeFilter,
                            () => dispatcher.BeginInvoke(new Action(OnAncestorChanged)));
                        break;
                    }
                }
            }

            private void OnAncestorChanged()
            {
                // Runs on the dispatcher thread, never inside the ancestor watch's own callback,
                // so disposing/replacing it here can't reenter its lock from the same wait.
                Rebuild();
                changed();
            }

            public void Dispose()
            {
                lock (gate)
                {
                    disposed = true;
                    leafWatch?.Dispose();
                    ancestorWatch?.Dispose();
                }
            }
        }
    }
}
