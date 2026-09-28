using System;
using System.Runtime.InteropServices;

namespace UltraWinBar.Utilities
{
    // Replaces ManagedShell's AppVisibilityHelper: its sink interface lacks PreserveSig, so every
    // launcher notification wrote a phantom [out, retval] through a garbage register (memory
    // corruption, eventually an access violation on the twinapi notify thread).
    internal sealed class LauncherVisibility : IDisposable
    {
        private static readonly Guid AppVisibilityClsid = new Guid("7E5FE3D9-985F-4908-91F9-EE19F9FD1514");
        private IAppVisibility _appVisibility;
        private Sink _sink;
        private uint _cookie;

        public event EventHandler Changed;

        public LauncherVisibility()
        {
            _appVisibility = (IAppVisibility)Activator.CreateInstance(Type.GetTypeFromCLSID(AppVisibilityClsid, true));
            _sink = new Sink(this);
            // Events are best effort; IsVisible still works without them.
            if (_appVisibility.Advise(_sink, out _cookie) != 0) _cookie = 0;
        }

        public bool IsVisible()
        {
            var appVisibility = _appVisibility ?? throw new ObjectDisposedException(nameof(LauncherVisibility));
            Marshal.ThrowExceptionForHR(appVisibility.IsLauncherVisible(out bool visible));
            return visible;
        }

        public void Dispose()
        {
            var appVisibility = _appVisibility;
            if (appVisibility == null) return;
            _appVisibility = null;
            try { if (_cookie != 0) appVisibility.Unadvise(_cookie); }
            catch (Exception) { }
            finally
            {
                _cookie = 0;
                _sink = null;
                Marshal.ReleaseComObject(appVisibility);
            }
        }

        // Called on a shell worker thread; must never let an exception reach native code.
        private sealed class Sink : IAppVisibilityEvents
        {
            private readonly WeakReference<LauncherVisibility> _owner;

            internal Sink(LauncherVisibility owner) => _owner = new WeakReference<LauncherVisibility>(owner);

            public int AppVisibilityOnMonitorChanged(IntPtr monitor, int previousMode, int currentMode) => 0;

            public int LauncherVisibilityChange(bool currentVisibleState)
            {
                try
                {
                    if (_owner.TryGetTarget(out var owner)) owner.Changed?.Invoke(owner, EventArgs.Empty);
                }
                catch (Exception) { }
                return 0;
            }
        }
    }

    [ComImport, Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAppVisibility
    {
        [PreserveSig] int GetAppVisibilityOnMonitor(IntPtr monitor, out int mode);
        [PreserveSig] int IsLauncherVisible([MarshalAs(UnmanagedType.Bool)] out bool visible);
        [PreserveSig] int Advise(IAppVisibilityEvents callback, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
    }

    // Public + ComVisible: the CCW only answers QueryInterface for COM-visible interfaces.
    [ComImport, ComVisible(true), Guid("6584CE6B-7D82-49C2-89C9-C6BC02BA8C38"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAppVisibilityEvents
    {
        [PreserveSig] int AppVisibilityOnMonitorChanged(IntPtr monitor, int previousMode, int currentMode);
        [PreserveSig] int LauncherVisibilityChange([MarshalAs(UnmanagedType.Bool)] bool currentVisibleState);
    }
}
