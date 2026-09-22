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
        private const string GlobalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
        private static string SessionPath => @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\" + Process.GetCurrentProcess().SessionId + @"\VirtualDesktops";
        private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
        private readonly List<RegistryWatch> watches = new List<RegistryWatch>();
        private IDesktopManager manager;
        private bool disposed;
        public static VirtualDesktopContext Instance { get; private set; }
        public Guid CurrentId { get; private set; }
        public event EventHandler Changed;

        public VirtualDesktopContext()
        {
            Instance = this;
            try { manager = (IDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"))); }
            catch (COMException) { }
            CurrentId = ReadCurrent();
            foreach (string path in new[] { GlobalPath, SessionPath })
            {
                var key = Registry.CurrentUser.OpenSubKey(path);
                if (key != null) watches.Add(new RegistryWatch(key, () => dispatcher.BeginInvoke(new Action(Refresh))));
            }
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
            Guid id = ReadCurrent();
            if (id == CurrentId) return;
            CurrentId = id;
            ShellLogger.Debug($"Virtual desktop changed: {id}");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Guid DesktopForWindow(IntPtr hwnd)
        {
            try
            {
                if (manager != null && manager.GetWindowDesktopId(hwnd, out Guid id) >= 0 && id != Guid.Empty) return id;
            }
            catch (COMException) { }
            return CurrentId;
        }

        public bool IsOnCurrentDesktop(IntPtr hwnd)
        {
            try { return manager == null || manager.IsWindowOnCurrentVirtualDesktop(hwnd, out bool current) < 0 || current; }
            catch (COMException) { return true; }
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var watch in watches) watch.Dispose();
            if (manager != null) Marshal.ReleaseComObject(manager);
            manager = null;
            Instance = null;
        }

        private sealed class RegistryWatch : IDisposable
        {
            private readonly RegistryKey key;
            private readonly AutoResetEvent signal = new AutoResetEvent(false);
            private readonly RegisteredWaitHandle wait;
            private readonly object gate = new object();
            private bool disposed;
            public RegistryWatch(RegistryKey key, Action changed)
            {
                this.key = key;
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
            private void Arm() => RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), false, 0x10000004,
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
    }
}
