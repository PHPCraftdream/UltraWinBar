using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ManagedShell.Common.Helpers;
using UltraWinBar.Utilities;
using System.Windows;
using ManagedShell.Common.Logging;
using Microsoft.Win32;
using ManagedShell.AppBar;
using System.Windows.Forms;
using ManagedShell.WindowsTray;
using System.Runtime.CompilerServices;
using System.IO;

namespace UltraWinBar
{
    /// <summary>
    /// Interaction logic for PropertiesWindow.xaml
    /// </summary>
    public partial class PropertiesWindow : Window, INotifyPropertyChanged
    {
        private static PropertiesWindow _instance;

        private readonly double _barSize;
        private readonly DictionaryManager _dictionaryManager;
        private readonly double _dpiScale;
        private readonly NotificationArea _notificationArea;
        private readonly AppBarScreen _screen;

        private FileSystemWatcher _themesWatcher;

        public event PropertyChangedEventHandler PropertyChanged;

        // Previews should always assume bottom edge
        public AppBarEdge AppBarEdge
        {
            get => AppBarEdge.Bottom;
        }

        // Previews should always assume horizontal orientation
        public Orientation Orientation
        {
            get => Orientation.Horizontal;
        }

        // Previews should always assume normal mode
        public AppBarMode AppBarMode
        {
            get => AppBarMode.Normal;
        }

        // Previews should reflect the locked setting
        public bool IsLocked
        {
            get => Settings.Instance.LockTaskbar;
        }

        // Previews should never be scaled
        public bool IsScaled
        {
            get => false;
        }

        // Previews should always assume 1 row
        public int Rows
        {
            get => 1;
        }

        private PropertiesWindow(NotificationArea notificationArea, DictionaryManager dictionaryManager, AppBarScreen screen, double dpiScale, double barSize)
        {
            _barSize = barSize;
            _dictionaryManager = dictionaryManager;
            _dpiScale = dpiScale;
            _notificationArea = notificationArea;
            _screen = screen;

            InitializeComponent();

            LoadPreviewHeight();
            LoadAutoStart();
            LoadLanguages();
            LoadOSSupport();
            LoadRows();
            LoadThemes();
            LoadWidth();
            LoadAppInfo();
            LoadClockActions();

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.LockTaskbar))
            {
                OnPropertyChanged(nameof(IsLocked));
                LoadPreviewHeight();
            }
            else if (e.PropertyName == nameof(Settings.Theme))
            {
                LoadPreviewHeight();
            }
            else if (e.PropertyName == nameof(Settings.Language))
            {
                LoadAppInfo();
                LoadClockActions();
            }
            else if (e.PropertyName == nameof(Settings.Edge) || e.PropertyName == nameof(Settings.AdditionalEdges))
            {
                RefreshEdgeCheckboxes();
            }
        }

        public static PropertiesWindow Open(NotificationArea notificationArea, DictionaryManager dictionaryManager, AppBarScreen screen, double dpiScale, double barSize)
        {
            if (_instance == null)
            {
                _instance = new PropertiesWindow(notificationArea, dictionaryManager, screen, dpiScale, barSize);
                _instance.UpdateWindowPosition();
                _instance.Show();
            }
            else
            {
                _instance.Activate();
            }

            return _instance;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadPreviewHeight()
        {
            double size = System.Windows.Application.Current.FindResource("TaskbarHeight") as double? ?? 0;

            if (!IsLocked)
            {
                size += System.Windows.Application.Current.FindResource("TaskbarUnlockedSize") as double? ?? 0;
            }

            TaskbarAppearancePreviewControl.Height = size;
            NotificationAreaPreviewControl.Height = size;
        }

        private void LoadAutoStart()
        {
            try
            {
                RegistryKey rKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                List<string> rKeyValueNames = rKey?.GetValueNames().ToList();

                if (rKeyValueNames != null)
                {
                    if (rKeyValueNames.Contains("UltraWinBar"))
                    {
                        cbAutoStart.IsChecked = true;
                    }
                    else
                    {
                        cbAutoStart.IsChecked = false;
                    }
                }
            }
            catch (Exception e)
            {
                ShellLogger.Error($"PropertiesWindow: Unable to load autorun setting from registry: {e.Message}");
            }
        }

        private void LoadLanguages()
        {
            foreach (var language in _dictionaryManager.GetLanguages())
            {
                cboLanguageSelect.Items.Add(language);
            }
        }

        private void LoadOSSupport()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter)
            {
                cbAllowBlurBehind.Visibility = Visibility.Collapsed;
                cbShowStartButtonMultiMon.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadRows()
        {
            for (int i = 1; i <= Settings.Instance.RowLimit; i++)
            {
                cboRowCount.Items.Add(i.ToString());
            }
        }

        private void LoadAppInfo()
        {
            gbUltraWinBar.Header = System.Windows.Forms.Application.ProductName;
            txtVersion.Text = string.Format((string)FindResource("version"), System.Windows.Forms.Application.ProductVersion);
        }

        private void LoadWidth()
        {
            sldTaskbarWidth.Maximum = Settings.Instance.TaskbarWidthLimit;
        }

        private void LoadThemes()
        {
            foreach (var theme in _dictionaryManager.GetThemes())
            {
                cboThemeSelect.Items.Add(theme);
            }
            try
            {
                string path = _dictionaryManager.GetThemeInstallDir();
                Directory.CreateDirectory(path);

                _themesWatcher = new FileSystemWatcher(_dictionaryManager.GetThemeInstallDir());
                _themesWatcher.Created += ThemesWatcher_Created;
                _themesWatcher.Deleted += ThemesWatcher_Deleted;
                _themesWatcher.Renamed += ThemesWatcher_Renamed;
                _themesWatcher.Filter = "*.xaml";
                _themesWatcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"Unable to watch custom themes directory: {e}");
            }
        }

        private void ThemesWatcher_Created(object sender, FileSystemEventArgs e)
        {
            string newTheme = Path.GetFileNameWithoutExtension(e.FullPath);
            Dispatcher.BeginInvoke(() => 
            {
                if (!cboThemeSelect.Items.Contains(newTheme))
                {
                    cboThemeSelect.Items.Add(newTheme);
                }
            });
        }

        private void ThemesWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            string removedTheme = Path.GetFileNameWithoutExtension(e.FullPath);
            Dispatcher.BeginInvoke(() =>
            {
                if (cboThemeSelect.Items.Contains(removedTheme))
                {
                    if (cboThemeSelect.SelectedItem is string selected && selected == removedTheme)
                    {
                        cboThemeSelect.SelectedIndex = 0;
                    }
                    cboThemeSelect.Items.Remove(removedTheme);
                }
            });
        }

        private void ThemesWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            string removedTheme = Path.GetFileNameWithoutExtension(e.OldFullPath);
            string newTheme = Path.GetFileNameWithoutExtension(e.FullPath);
            Dispatcher.BeginInvoke(() =>
            {
                if (cboThemeSelect.Items.Contains(removedTheme))
                {
                    if (cboThemeSelect.SelectedItem is string selected && selected == removedTheme)
                    {
                        cboThemeSelect.SelectedIndex = 0;
                    }
                    cboThemeSelect.Items.Remove(removedTheme);
                }
                if (!cboThemeSelect.Items.Contains(newTheme))
                {
                    cboThemeSelect.Items.Add(newTheme);
                }
            });
        }

        private void UpdateWindowPosition()
        {
            Rect area = WindowPlacementGuard.AvailableArea(_screen.Bounds);
            double scale = _dpiScale;
            MaxWidth = Math.Max(100, area.Width / scale - 20);
            MaxHeight = Math.Max(100, area.Height / scale - 20);
            MinWidth = Math.Min(360, MaxWidth);
            MinHeight = Math.Min(300, MaxHeight);
            Width = Math.Min(Width, MaxWidth);
            Height = Math.Min(Height, MaxHeight);
            double width = Width;
            double height = Height;
            Left = area.Left / scale + Math.Max(0, (area.Width / scale - width) / 2);
            Top = area.Top / scale + Math.Max(0, (area.Height / scale - height) / 2);
        }

        private void OK_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetQuickLaunchLocation_OnClick(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = (string)FindResource("quick_launch_folder");
#if NETCOREAPP3_0_OR_GREATER
            fbd.UseDescriptionForTitle = true;
#endif
            fbd.ShowNewFolderButton = false;
            fbd.SelectedPath = Settings.Instance.QuickLaunchPath;

            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Settings.Instance.QuickLaunchPath = fbd.SelectedPath;
            }
        }

        private void PropertiesWindow_OnClosing(object sender, CancelEventArgs e)
        {
            _instance = null;
            if (_themesWatcher != null)
            {
                _themesWatcher.Created -= ThemesWatcher_Created;
                _themesWatcher.Deleted -= ThemesWatcher_Deleted;
                _themesWatcher.Renamed -= ThemesWatcher_Renamed;
            }
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }

        private void PropertiesWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateWindowPosition();

            RefreshEdgeCheckboxes();
        }

        #region Multiple taskbars
        private bool _isUpdatingEdgeCheckboxes;

        private System.Collections.Generic.IEnumerable<(AppBarEdge Edge, System.Windows.Controls.CheckBox CheckBox)> EdgeCheckboxes()
        {
            yield return (AppBarEdge.Left, cbEdgeLeft);
            yield return (AppBarEdge.Top, cbEdgeTop);
            yield return (AppBarEdge.Right, cbEdgeRight);
            yield return (AppBarEdge.Bottom, cbEdgeBottom);
        }

        /// <summary>
        /// Reflects Settings' current primary + additional edges onto the checkboxes,
        /// without re-entering EdgeCheckBox_Changed.
        /// </summary>
        private void RefreshEdgeCheckboxes()
        {
            _isUpdatingEdgeCheckboxes = true;

            var enabled = Settings.Instance.EnabledEdges;
            foreach (var (edge, checkBox) in EdgeCheckboxes())
            {
                checkBox.IsChecked = enabled.Contains(edge);
            }

            _isUpdatingEdgeCheckboxes = false;
        }

        private void EdgeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEdgeCheckboxes)
            {
                return;
            }

            var checkedEdges = EdgeCheckboxes()
                .Where(x => x.CheckBox.IsChecked == true)
                .Select(x => x.Edge)
                .ToList();

            if (checkedEdges.Count == 0)
            {
                // Must always have at least one taskbar; revert the change.
                RefreshEdgeCheckboxes();
                return;
            }

            // Keep the existing primary edge if it's still checked, so unrelated
            // taskbars don't get needlessly recreated; otherwise promote another.
            AppBarEdge primary = checkedEdges.Contains(Settings.Instance.Edge)
                ? Settings.Instance.Edge
                : checkedEdges[0];

            checkedEdges.Remove(primary);

            Settings.Instance.Edge = primary;
            Settings.Instance.AdditionalEdges = checkedEdges;
        }

        private void ResetTaskAssignments_OnClick(object sender, RoutedEventArgs e)
        {
            Settings.Instance.TaskbarAssignments = new System.Collections.Generic.List<TaskbarAssignment>();
        }
        #endregion

        private void PropertiesWindow_OnContentRendered(object sender, EventArgs e)
        {
            UpdateWindowPosition();
        }

        private void CbAutoStart_OnChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                RegistryKey rKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                var chkBox = (System.Windows.Controls.CheckBox)sender;

                if (chkBox.IsChecked.Equals(false))
                {
                    rKey?.DeleteValue("UltraWinBar");
                }
                else
                {
                    // Registry Run values are command lines; quote the executable path to handle spaces in usernames/paths.
                    rKey?.SetValue("UltraWinBar", $"\"{ExePath.GetExecutablePath()}\"");
                }
            }
            catch (Exception exception)
            {
                ShellLogger.Error($"PropertiesWindow: Unable to update registry autorun setting: {exception.Message}");
            }
        }

        private void LoadClockActions()
        {
            if (EnvironmentHelper.IsWindows10OrBetter)
            {
                return;
            }

            // Remove options unsupported prior to Windows 10.
            var availableClockActions = (FindResource("clock_click_action_values") as Array)?.Cast<object>().ToList();
            if (availableClockActions == null)
            {
                return;
            }

            availableClockActions.RemoveAt((int)ClockClickOption.OpenNotificationCenter);
            availableClockActions.RemoveAt((int)ClockClickOption.OpenModernCalendar);
            cboClockAction.ItemsSource = availableClockActions;
        }

        private void CboEdgeSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboEdgeSelect.SelectedItem == null)
            {
                cboEdgeSelect.SelectedValue = cboEdgeSelect.Items[(int)Settings.Instance.Edge];
            }
        }

        private void CboMultiMonMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboMultiMonMode.SelectedItem == null)
            {
                cboMultiMonMode.SelectedValue = cboMultiMonMode.Items[(int)Settings.Instance.MultiMonMode];
            }
        }

        private void CboInvertIconsMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboInvertIconsMode.SelectedItem == null)
            {
                cboInvertIconsMode.SelectedValue = cboInvertIconsMode.Items[(int)Settings.Instance.InvertIconsMode];
            }
        }

        private void CboMiddleMouseAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboMiddleMouseAction.SelectedItem == null)
            {
                cboMiddleMouseAction.SelectedValue = cboMiddleMouseAction.Items[(int)Settings.Instance.TaskMiddleClickAction];
            }
        }

        private void CboClockAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboClockAction.SelectedItem == null)
            {
                cboClockAction.SelectedValue = cboClockAction.Items[(int)Settings.Instance.ClockClickAction];
            }
        }

        private void CboTaskWheelAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboTaskWheelAction.SelectedItem == null)
            {
                cboTaskWheelAction.SelectedValue = cboTaskWheelAction.Items[(int)Settings.Instance.TaskWheelAction];
            }
        }

        private void CboRowCount_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboRowCount.SelectedItem == null)
            {
                cboRowCount.SelectedValue = cboRowCount.Items[Settings.Instance.PrimaryRowCount - 1];
            }
        }

        private void CboWinNumHotkeysAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (this != _instance)
            {
                // Don't pop a message box if we're closed but haven't been GC'd yet
                return;
            }
            if (cboWinNumHotkeysAction.SelectedItem == null)
            {
                cboWinNumHotkeysAction.SelectedValue = cboWinNumHotkeysAction.Items[(int)Settings.Instance.WinNumHotkeysAction];
            }
            else if (e.RemovedItems.Count > 0 && e.RemovedItems[0] == cboWinNumHotkeysAction.Items[0])
            {
                System.Windows.MessageBox.Show((string)System.Windows.Application.Current.FindResource("hotkey_warning_text"), (string)System.Windows.Application.Current.FindResource("hotkey_warning_title"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CustomizeNotifications_OnClick(object sender, RoutedEventArgs e)
        {
            OpenCustomizeNotifications();
        }

        public void OpenCustomizeNotifications()
        {
            NotificationPropertiesWindow.Open(_notificationArea, new Point(Left, Top));
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            ShellHelper.ExecuteProcess(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private void OpenCustomThemesFolder_OnClick(object sender, RoutedEventArgs e)
        {
            string path = _dictionaryManager.GetThemeInstallDir();
            Directory.CreateDirectory(path);
            ShellHelper.StartProcess(path);
        }

        private void ClockFormatHelpButton_Click(object sender, RoutedEventArgs e)
        {
            ShellHelper.ExecuteProcess("https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings#table");
        }
    }
}
