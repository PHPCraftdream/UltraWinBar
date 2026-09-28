using System;
using System.Runtime.InteropServices;
using ManagedShell.Common.Enums;

namespace ManagedShell.Common.Interfaces
{
    [ComImport, Guid("6584CE6B-7D82-49C2-89C9-C6BC02BA8C38"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    // UltraWinBar: implemented by a managed sink (CCW). Without PreserveSig every callback wrote
    // the `long` result through a garbage [out, retval] pointer and eventually crashed the process.
    public interface IAppVisibilityEvents
    {
        [PreserveSig] int AppVisibilityOnMonitorChanged(
            [In] IntPtr hMonitor,
            [In] MONITOR_APP_VISIBILITY previousMode,
            [In] MONITOR_APP_VISIBILITY currentMode);

        [PreserveSig] int LauncherVisibilityChange([In, MarshalAs(UnmanagedType.Bool)] bool currentVisibleState);
    }
}
