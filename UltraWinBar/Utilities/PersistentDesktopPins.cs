using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    internal sealed class PersistentDesktopPins : IDisposable
    {
        private readonly Tasks tasks;
        private readonly TasksService service;
        private readonly VirtualDesktopContext desktops;
        private readonly INotifyCollectionChanged windows;
        private bool queued;
        private bool disposed;

        internal PersistentDesktopPins(Tasks tasks, TasksService service, VirtualDesktopContext desktops)
        {
            this.tasks = tasks;
            this.service = service;
            this.desktops = desktops;
            windows = tasks.GroupedWindows.SourceCollection as INotifyCollectionChanged;
            if (windows != null) windows.CollectionChanged += WindowsChanged;
            service.WindowActivated += WindowActivated;
            desktops.Changed += DesktopChanged;
            Settings.Instance.PropertyChanged += SettingsChanged;
            Queue();
        }

        private void WindowsChanged(object sender, NotifyCollectionChangedEventArgs e) => Queue();
        private void WindowActivated(object sender, WindowEventArgs e) => Queue();
        private void DesktopChanged(object sender, EventArgs e) => Queue();
        private void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.AllDesktopApplications)) Queue();
        }

        private void Queue()
        {
            if (queued || disposed || !DesktopActions.IsSupported) return;
            queued = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                queued = false;
                if (disposed) return;
                Restore();
            }), DispatcherPriority.ContextIdle);
        }

        private void Restore()
        {
            try
            {
                using var actions = new DesktopActions();
                if (!Settings.Instance.DesktopPinPreferencesInitialized)
                {
                    var ids = Settings.Instance.AllDesktopApplications.ToList();
                    foreach (var window in tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>().ToArray())
                    {
                        try
                        {
                            string id = actions.GetApplicationId(window.Handle);
                            if (actions.IsApplicationIdPinned(id) && !ids.Contains(id)) ids.Add(id);
                        }
                        catch (Exception error) { ShellLogger.Debug($"Desktop pin import skipped {window.Handle}: {error.Message}"); }
                    }
                    Settings.Instance.AllDesktopApplications = ids;
                    Settings.Instance.DesktopPinPreferencesInitialized = true;
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
            service.WindowActivated -= WindowActivated;
            desktops.Changed -= DesktopChanged;
            Settings.Instance.PropertyChanged -= SettingsChanged;
        }
    }
}
