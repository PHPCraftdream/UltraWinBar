using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;
using UltraWinBar.Utilities;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace UltraWinBar
{
    public partial class Taskbar
    {
        #region Context menu
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (_updater.IsUpdateAvailable)
            {
                UpdateAvailableMenuItem.Visibility = Visibility.Visible;
            }

            if (NativeMethods.GetAsyncKeyState((int)System.Windows.Forms.Keys.ShiftKey) < 0 && Settings.Instance.ShowExitMenuItem)
            {
                RestartMenuItem.Visibility = Visibility.Visible;
            }
            else
            {
                RestartMenuItem.Visibility = Visibility.Collapsed;
            }

            StretchMenuItem.Visibility = Settings.Instance.EnabledEdges.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            MakeMainMenuItem.Visibility = Settings.Instance.EnabledEdges.Count > 1 && Settings.Instance.ResolvedDefaultTaskEdge != AppBarEdge
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void StretchMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var priority = new System.Collections.Generic.List<AppBarEdge>(Settings.Instance.EdgePriority);
            priority.Remove(AppBarEdge);
            priority.Insert(0, AppBarEdge);
            Settings.Instance.EdgePriority = priority;
        }

        private void MakeMainMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Settings.Instance.DefaultTaskEdge = AppBarEdge;
        }

        private void SetTimeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ShellHelper.StartProcess("timedate.cpl");
        }

        private void CustomizeNotificationsMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            PropertiesWindow propWindow = PropertiesWindow.Open(_shellManager.NotificationArea, _dictionaryManager, Screen, DpiScale, Orientation == Orientation.Horizontal ? DesiredHeight : DesiredWidth);
            propWindow.OpenCustomizeNotifications();
        }

        private void TaskManagerMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ShellHelper.StartTaskManager();
        }

        private void OpenSystemSettingsMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            StartShellTarget("ms-settings:");
        }

        private void OpenComputerManagementMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            StartShellTarget("compmgmt.msc");
        }

        private void ThisPcMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            StartExplorer("shell:MyComputerFolder");
        }

        private void OpenDrivesMenuItem_OnSubmenuOpened(object sender, RoutedEventArgs e)
        {
            while (OpenDrivesMenuItem.Items.Count > 2)
            {
                OpenDrivesMenuItem.Items.RemoveAt(2);
            }

            try
            {
                foreach (System.IO.DriveInfo drive in System.IO.DriveInfo.GetDrives())
                {
                    var driveMenuItem = new MenuItem { Header = drive.Name, Tag = drive.Name };
                    driveMenuItem.Click += DriveMenuItem_OnClick;
                    OpenDrivesMenuItem.Items.Add(driveMenuItem);
                }
            }
            catch (Exception error)
            {
                ShellLogger.Warning($"Unable to enumerate drives for the taskbar menu: {error.Message}");
            }
        }

        private void DriveMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string root)
            {
                StartExplorer(root);
            }
        }

        private static void StartShellTarget(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception error)
            {
                ShellLogger.Error($"Unable to open {target} from the taskbar menu.", error);
            }
        }

        private static void StartExplorer(string root)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add(root);
                Process.Start(startInfo);
            }
            catch (Exception error)
            {
                ShellLogger.Error($"Unable to open Explorer at {root} from the taskbar menu.", error);
            }
        }

        private void UpdateAvailableMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _updater.DownloadUrl,
                UseShellExecute = true
            };

            Process.Start(psi);
        }

        private void PropertiesMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            PropertiesWindow.Open(_shellManager.NotificationArea, _dictionaryManager, Screen, DpiScale, Orientation == Orientation.Horizontal ? DesiredHeight : DesiredWidth);
        }

        private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).ExitGracefully();
        }

        private void RestartMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).RestartApp();
        }
        #endregion
    }
}
