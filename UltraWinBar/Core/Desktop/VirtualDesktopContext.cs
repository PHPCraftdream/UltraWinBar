using ManagedShell.Common.Logging;
using ManagedShell.Common.Native;
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
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly List<RegistryTreeWatch> watches = new List<RegistryTreeWatch>();
        // Per-dispatcher-pass cache for GetAssignedEdge's DesktopForWindow lookups; UI thread only.
        private readonly Dictionary<IntPtr, Guid> desktopForWindowCache = new Dictionary<IntPtr, Guid>();
        private bool desktopForWindowCacheClearQueued;
        // Explorer-hosted: severed on Explorer restarts, recreated lazily with backoff.
        private readonly ShellComProxy<IDesktopManager> manager;
        // R9-I (K14): explicit state instead of a bare bool. No restart caller exists for this
        // type (constructed once in App.xaml.cs, disposed once at shutdown) - unlike TasksService,
        // there is no Start method to guard against reuse after Dispose.
        internal ServiceLifecycleState LifecycleState { get; private set; } = ServiceLifecycleState.Created;
        private bool disposed => LifecycleState == ServiceLifecycleState.Disposed;
        public static VirtualDesktopContext Instance { get; private set; }
        public Guid CurrentId { get; private set; }
        public event EventHandler Changed;
        // Raised when the manager is recreated after a null period, so panels re-filter.
        public event EventHandler ManagerRecovered;

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
            manager = new ShellComProxy<IDesktopManager>("Virtual desktop manager", CreateManager,
                stale => Marshal.ReleaseComObject(stale));
            manager.Dropped += () => desktopForWindowCache.Clear();
            // Always after a null period (the constructor's first manager does not count): panels re-filter.
            manager.Recovered += () =>
            {
                desktopForWindowCache.Clear();
                dispatcher.BeginInvoke(new Action(() => ManagerRecovered?.Invoke(this, EventArgs.Empty)));
            };
            CurrentId = ReadCurrent();
            foreach (string path in new[] { GlobalPath, SessionPath })
                watches.Add(new RegistryTreeWatch(path, dispatcher, () => dispatcher.BeginInvoke(new Action(Refresh))));
            ShellLogger.Info($"Virtual desktop: {CurrentId}");
            LifecycleState = ServiceLifecycleState.Running;
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
            desktopForWindowCache.Clear();
            ShellLogger.Debug($"Virtual desktop changed: {id}");
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static IDesktopManager CreateManager() =>
            (IDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClsid));

        public bool TryGetWindowDesktopId(IntPtr hwnd, out Guid id)
        {
            id = Guid.Empty;
            var current = manager.Get();
            try { return current != null && manager.Check(current.GetWindowDesktopId(hwnd, out id)) >= 0 && id != Guid.Empty; }
            catch (Exception error) when (error is COMException || error is InvalidComObjectException)
            {
                manager.Report(error);
                return false;
            }
        }

        public Guid DesktopForWindow(IntPtr hwnd)
        {
            if (TryGetWindowDesktopId(hwnd, out Guid id)) return id;
            return CurrentId;
        }

        // GetAssignedEdge only: one COM lookup per window per dispatcher pass instead of per panel.
        internal Guid DesktopForWindowCached(IntPtr hwnd)
        {
            if (desktopForWindowCache.TryGetValue(hwnd, out Guid cached)) return cached;
            Guid id = DesktopForWindow(hwnd);
            desktopForWindowCache[hwnd] = id;
            QueueDesktopForWindowCacheClear();
            return id;
        }

        internal void ForgetWindowDesktop(IntPtr hwnd) => desktopForWindowCache.Remove(hwnd);

        private void QueueDesktopForWindowCacheClear()
        {
            if (desktopForWindowCacheClearQueued) return;
            desktopForWindowCacheClearQueued = true;
            dispatcher.BeginInvoke(new Action(() =>
            {
                desktopForWindowCacheClearQueued = false;
                desktopForWindowCache.Clear();
            }), DispatcherPriority.ContextIdle);
        }

        public bool TryMoveWindowToDesktop(IntPtr hwnd, Guid destination)
        {
            if (hwnd == IntPtr.Zero || destination == Guid.Empty) return false;
            var current = manager.Get();
            try
            {
                if (current != null && manager.Check(current.MoveWindowToDesktop(hwnd, ref destination)) >= 0)
                {
                    desktopForWindowCache.Remove(hwnd);
                    return true;
                }
            }
            catch (Exception error) when (error is COMException || error is InvalidComObjectException)
            {
                ShellLogger.Warning($"Desktop move COM failed for {hwnd}: {error.Message}");
                manager.Report(error);
            }

            if (!DesktopActions.IsSupported) return false;
            try
            {
                using var actions = new DesktopActions();
                actions.MoveWindow(hwnd, destination);
                desktopForWindowCache.Remove(hwnd);
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
            var current = manager.Get();
            try { return current == null || manager.Check(current.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrent)) < 0 || onCurrent; }
            catch (Exception error) when (error is COMException || error is InvalidComObjectException)
            {
                manager.Report(error);
                return true;
            }
        }

        // R9-I (K14): idempotent - a second Dispose() call is a no-op.
        public void Dispose()
        {
            if (LifecycleState == ServiceLifecycleState.Disposed) return;
            LifecycleState = ServiceLifecycleState.Disposed;
            foreach (var watch in watches) watch.Dispose();
            manager.Dispose();
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
                    try
                    {
                        lock (gate)
                        {
                            if (disposed) return;
                            Arm();
                            changed();
                        }
                    }
                    catch (Exception error) { CallbackGuard.Report("Virtual desktop registry watch", error); }
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
