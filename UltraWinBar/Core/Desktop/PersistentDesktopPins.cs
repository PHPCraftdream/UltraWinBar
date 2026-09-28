using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    internal sealed class PersistentDesktopPins : IDisposable
    {
        private readonly Tasks tasks;
        private readonly VirtualDesktopContext desktops;
        private readonly INotifyCollectionChanged windows;
        private readonly List<ApplicationWindow> pendingWindows = new List<ApplicationWindow>();
        private const int MaxImportScanAttempts = 5;
        private int importScanAttempts;
        private bool queued;
        private bool fullRestore;
        private bool disposed;

        internal PersistentDesktopPins(Tasks tasks, VirtualDesktopContext desktops)
        {
            this.tasks = tasks;
            this.desktops = desktops;
            windows = tasks.GroupedWindows.SourceCollection as INotifyCollectionChanged;
            if (windows != null) windows.CollectionChanged += WindowsChanged;
            // Not desktops.Changed: Windows keeps all-desktop pins across switches, so a plain
            // desktop switch never needs a restore. Only a manager recreation (Explorer restart,
            // virtual desktop service reconnect) can have dropped pins made while it was down.
            desktops.ManagerRecovered += ManagerRecovered;
            Settings.Instance.PropertyChanged += SettingsChanged;
            fullRestore = true;
            Queue();
        }

        // App-id pins only need restoring after an Explorer reset or when a remembered app gets a new window.
        internal static bool RequiresPinRestore(NotifyCollectionChangedAction action) =>
            action == NotifyCollectionChangedAction.Reset || action == NotifyCollectionChangedAction.Add;

        private void WindowsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!RequiresPinRestore(e.Action)) return;
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                fullRestore = true;
                Queue();
                return;
            }
            // Add: skip entirely unless a remembered app id exists and one of the new windows might own it.
            if (e.NewItems == null || Settings.Instance.AllDesktopApplications.Count == 0) return;
            foreach (var window in e.NewItems.OfType<ApplicationWindow>()) pendingWindows.Add(window);
            if (pendingWindows.Count > 0) Queue();
        }
        // Manager was recreated after being null (Explorer restart or service reconnect): pins
        // set while it was unavailable never landed, so a full restore is worth its COM cost here.
        private void ManagerRecovered(object sender, EventArgs e)
        {
            fullRestore = true;
            Queue();
        }
        private void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Settings.AllDesktopApplications)) return;
            fullRestore = true;
            Queue();
        }

        private void Queue()
        {
            if (queued || disposed || !DesktopActions.IsSupported) return;
            queued = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                queued = false;
                if (disposed) return;
                if (fullRestore)
                {
                    fullRestore = false;
                    pendingWindows.Clear();
                    Restore();
                }
                else
                {
                    RestorePending();
                }
            }), DispatcherPriority.ContextIdle);
        }

        // Only touches DesktopActions/COM when a newly added window's app id is actually remembered.
        private void RestorePending()
        {
            if (pendingWindows.Count == 0) return;
            var addedWindows = pendingWindows.ToArray();
            pendingWindows.Clear();
            var remembered = Settings.Instance.AllDesktopApplications;
            if (remembered.Count == 0) return;
            try
            {
                using var actions = new DesktopActions();
                foreach (var window in addedWindows)
                {
                    if (!IsWindow(window.Handle)) continue;
                    string id;
                    try
                    {
                        id = actions.GetApplicationId(window.Handle);
                    }
                    catch (Exception error)
                    {
                        ShellLogger.Debug($"Desktop pin id lookup skipped {window.Handle}: {error.Message}");
                        continue;
                    }
                    if (!remembered.Contains(id)) continue;
                    try
                    {
                        if (actions.IsApplicationIdPinned(id)) continue;
                        actions.SetApplicationIdPinned(id, true);
                        ShellLogger.Info($"Desktop pin: restored application {id}");
                    }
                    catch (Exception error) { ShellLogger.Warning($"Desktop pin restore failed for {id}: {error.Message}"); }
                }
            }
            catch (Exception error) { ShellLogger.Warning($"Desktop pins unavailable: {error.Message}"); }
        }

        // A window whose id lookup never succeeds (e.g. the virtual desktop service is briefly
        // down at startup) must still let the scan retry; a persistently failing window must not
        // keep it incomplete forever, so give up and keep the partial result after maxAttempts.
        internal static bool ShouldFinalizeImportScan(bool scanComplete, int attempts, int maxAttempts) =>
            scanComplete || attempts >= maxAttempts;

        private void Restore()
        {
            try
            {
                using var actions = new DesktopActions();
                if (!Settings.Instance.DesktopPinPreferencesInitialized)
                {
                    var ids = Settings.Instance.AllDesktopApplications.ToList();
                    bool scanComplete = true;
                    foreach (var window in tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>().ToArray())
                    {
                        try
                        {
                            string id = actions.GetApplicationId(window.Handle);
                            if (actions.IsApplicationIdPinned(id) && !ids.Contains(id)) ids.Add(id);
                        }
                        catch (Exception error)
                        {
                            scanComplete = false;
                            ShellLogger.Debug($"Desktop pin import skipped {window.Handle}: {error.Message}");
                        }
                    }
                    importScanAttempts = scanComplete ? 0 : importScanAttempts + 1;
                    if (ShouldFinalizeImportScan(scanComplete, importScanAttempts, MaxImportScanAttempts))
                    {
                        if (!scanComplete) ShellLogger.Warning($"Desktop pin import gave up after {importScanAttempts} attempts; using partial results.");
                        Settings.Instance.AllDesktopApplications = ids;
                        Settings.Instance.DesktopPinPreferencesInitialized = true;
                    }
                }
                foreach (string id in Settings.Instance.AllDesktopApplications.ToArray())
                {
                    try
                    {
                        if (actions.IsApplicationIdPinned(id)) continue;
                        actions.SetApplicationIdPinned(id, true);
                        ShellLogger.Info($"Desktop pin: restored application {id}");
                    }
                    catch (Exception error) { ShellLogger.Warning($"Desktop pin restore failed for {id}: {error.Message}"); }
                }
            }
            catch (Exception error) { ShellLogger.Warning($"Desktop pins unavailable: {error.Message}"); }
        }

        public void Dispose()
        {
            disposed = true;
            if (windows != null) windows.CollectionChanged -= WindowsChanged;
            desktops.ManagerRecovered -= ManagerRecovered;
            Settings.Instance.PropertyChanged -= SettingsChanged;
        }
    }
}
