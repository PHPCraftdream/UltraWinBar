using System;
using System.Runtime.InteropServices;
using ManagedShell.Common.Enums;
using ManagedShell.Common.Interfaces;
using ManagedShell.Common.Logging;

namespace ManagedShell.Common.SupportingClasses
{
    class AppVisibilityEvents : IAppVisibilityEvents
    {
        internal event EventHandler<AppVisibilityEventArgs> AppVisibilityChanged;
        internal event EventHandler<LauncherVisibilityEventArgs> LauncherVisibilityChanged;

        public AppVisibilityEvents() { }

        // UltraWinBar: HRESULT returns (PreserveSig) and no exception may cross back into native code.
        public int AppVisibilityOnMonitorChanged([In] IntPtr hMonitor, [In] MONITOR_APP_VISIBILITY previousMode, [In] MONITOR_APP_VISIBILITY currentMode)
        {
            try
            {
                AppVisibilityEventArgs args = new AppVisibilityEventArgs
                {
                    MonitorHandle = hMonitor,
                    PreviousMode = previousMode,
                    CurrentMode = currentMode
                };

                AppVisibilityChanged?.Invoke(this, args);
            }
            catch (Exception e)
            {
                ShellLogger.Error($"AppVisibilityEvents: AppVisibilityChanged handler failed: {e.Message}");
            }
            return 0;
        }

        public int LauncherVisibilityChange([In] bool currentVisibleState)
        {
            try
            {
                LauncherVisibilityEventArgs args = new LauncherVisibilityEventArgs
                {
                    Visible = currentVisibleState
                };

                LauncherVisibilityChanged?.Invoke(this, args);
            }
            catch (Exception e)
            {
                ShellLogger.Error($"AppVisibilityEvents: LauncherVisibilityChanged handler failed: {e.Message}");
            }
            return 0;
        }
    }
}
