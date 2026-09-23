using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using UltraWinBar.Utilities;

namespace UltraWinBar.Controls
{
    public partial class TaskButton
    {
        private void BuildDesktopMenus()
        {
            MoveWindowDesktopMenuItem.Items.Clear();
            MoveAppDesktopMenuItem.Items.Clear();
            bool enabled = Window != null && DesktopActions.IsSupported;
            AllDesktopsMenuItem.IsEnabled = MoveWindowDesktopMenuItem.IsEnabled = MoveAppDesktopMenuItem.IsEnabled = enabled;
            AllDesktopsMenuItem.IsChecked = false;
            if (!enabled) return;
            try
            {
                using var actions = new DesktopActions();
                bool pinned = actions.IsApplicationPinned(Window.Handle);
                bool remembered = Settings.Instance.AllDesktopApplications.Contains(actions.GetApplicationId(Window.Handle));
                AllDesktopsMenuItem.IsChecked = pinned || remembered;
                MoveWindowDesktopMenuItem.IsEnabled = MoveAppDesktopMenuItem.IsEnabled = !pinned;
                int index = 0;
                foreach (var desktop in DesktopActions.GetDesktops())
                {
                    index++;
                    string name = desktop.Name ?? string.Format((string)FindResource("desktop_number"), index);
                    foreach (bool wholeApp in new[] { false, true })
                    {
                        var entry = new MenuItem { Header = new TextBlock { Text = name } };
                        entry.Click += (_, __) => RunDesktopAction(() => MoveToDesktop(desktop.Id, wholeApp));
                        (wholeApp ? MoveAppDesktopMenuItem : MoveWindowDesktopMenuItem).Items.Add(entry);
                    }
                }
            }
            catch (Exception error)
            {
                AllDesktopsMenuItem.IsEnabled = MoveWindowDesktopMenuItem.IsEnabled = MoveAppDesktopMenuItem.IsEnabled = false;
                ShellLogger.Warning($"Desktop menu unavailable: {error.Message}");
            }
        }

        private void AllDesktops_OnClick(object sender, RoutedEventArgs e)
        {
            bool requested = AllDesktopsMenuItem.IsChecked;
            RunDesktopAction(() =>
            {
                using var actions = new DesktopActions();
                var edge = TaskAssignmentManager.GetAssignedEdge(Window) ?? Settings.Instance.ResolvedDefaultTaskEdge;
                string appId = actions.GetApplicationId(Window.Handle);
                actions.SetApplicationPinned(Window.Handle, requested);
                var preferences = Settings.Instance.AllDesktopApplications.Where(value => value != appId).ToList();
                if (requested) preferences.Add(appId);
                Settings.Instance.AllDesktopApplications = preferences;
                string id = TaskAssignmentManager.GetIdentifier(Window, TaskAssignmentMode.ExecutablePath);
                if (requested && id != null)
                {
                    var rules = Settings.Instance.TaskbarAssignments.Where(rule =>
                        !(rule.DesktopId == Guid.Empty && rule.Mode == TaskAssignmentMode.ExecutablePath && rule.Identifier == id)).ToList();
                    rules.Add(new TaskbarAssignment { DesktopId = Guid.Empty, Identifier = id,
                        Mode = TaskAssignmentMode.ExecutablePath, Edge = edge });
                    Settings.Instance.TaskbarAssignments = rules;
                }
            });
        }

        private void MoveToDesktop(Guid desktop, bool wholeApp)
        {
            string identifier = TaskAssignmentManager.GetIdentifier(Window, TaskAssignmentMode.ExecutablePath);
            var windows = wholeApp
                ? Host.Tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>()
                    .Where(w => identifier != null && TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath) == identifier).ToArray()
                : new[] { Window };
            using var actions = new DesktopActions();
            int failed = 0;
            foreach (var window in windows)
            {
                try
                {
                    var edge = TaskAssignmentManager.GetAssignedEdge(window) ?? Settings.Instance.ResolvedDefaultTaskEdge;
                    actions.MoveWindow(window.Handle, desktop);
                    TaskAssignmentManager.AssignToEdge(window, edge, TaskAssignmentMode.WindowClassAndTitle);
                }
                catch (Exception error)
                {
                    failed++;
                    ShellLogger.Warning($"Desktop move failed for {window.Handle}: {error.Message}");
                }
            }
            if (failed > 0) throw new InvalidOperationException($"{failed}/{windows.Length} windows were not moved.");
        }

        private void RunDesktopAction(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                ShellLogger.Error($"Desktop operation failed: {error}");
                MessageBox.Show((string)FindResource("desktop_action_failed"), "UltraWinBar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                foreach (var window in Host.Tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>())
                    window.SetShowInTaskbar();
                foreach (var panel in Application.Current.Windows.OfType<Taskbar>())
                    panel.TaskListControl.RefreshWindowVisibility();
            }
        }
    }
}
