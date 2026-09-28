using System;
using System.Runtime.InteropServices;
using ManagedShell.Common.Enums;

namespace ManagedShell.Common.Interfaces
{
    [ComImport, Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    // UltraWinBar: PreserveSig + BOOL; `long` without PreserveSig passed a hidden retval pointer
    // and `out bool` marshalled as 2-byte VARIANT_BOOL while Windows writes a 4-byte BOOL.
    public interface IAppVisibility
    {
        [PreserveSig] int GetAppVisibilityOnMonitor([In] IntPtr hMonitor, [Out] out MONITOR_APP_VISIBILITY pMode);
        [PreserveSig] int IsLauncherVisible([Out, MarshalAs(UnmanagedType.Bool)] out bool pfVisible);
        [PreserveSig] int Advise([In] IAppVisibilityEvents pCallback, [Out] out int pdwCookie);
        [PreserveSig] int Unadvise([In] int dwCookie);
    }
}
