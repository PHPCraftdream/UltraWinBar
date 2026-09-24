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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace UltraWinBar
{
    /// <summary>
    /// Interaction logic for Taskbar.xaml
    /// </summary>
    public partial class Taskbar : AppBarWindow
    {
        public bool IsLocked => Settings.Instance.LockTaskbar;

        public bool IsScaled => DpiScale > 1 || Settings.Instance.TaskbarScale > 1;

        private double _unlockedMargin;
        public double DesiredRowHeight { get; private set; }

        public int Rows
        {
            get => Settings.Instance.GetEdgeSize(AppBarEdge, Settings.Instance.RowCount);
            set => Settings.Instance.SetEdgeSize(AppBarEdge, value);
        }

        public int TaskbarWidthCount
        {
            get => Settings.Instance.GetEdgeSize(AppBarEdge, Settings.Instance.TaskbarWidth);
            set => Settings.Instance.SetEdgeSize(AppBarEdge, value);
        }

        private bool _startMenuOpen;
        private LowLevelMouseHook _mouseDragHook;
        private Point? _mouseDragStart = null;
        private bool _mouseDragResize = false;
        private readonly DictionaryManager _dictionaryManager;
        private readonly ShellManager _shellManager;
        private readonly StartMenuMonitor _startMenuMonitor;
        private readonly Updater _updater;
        private bool _fullScreenSuppressed;
        private int _openMenus;
        private readonly AppBarMode _startupAppBarMode;
        private bool _registrationDeferred;
        private NativeMethods.Rect? _standaloneBounds;
        
        public WindowManager windowManager;
        public HotkeyManager hotkeyManager;

        /// <summary>
        /// True for the taskbar on Settings.Edge. Additional taskbars own their edge
        /// independently and must not follow changes to the primary edge.
        /// </summary>
        public bool IsPrimaryEdge { get; }

        public Taskbar(WindowManager windowManager, DictionaryManager dictionaryManager, ShellManager shellManager, StartMenuMonitor startMenuMonitor, Updater updater, HotkeyManager hotkeyManager, AppBarScreen screen, AppBarEdge edge, AppBarMode mode, bool isPrimaryEdge = true, bool deferRegistration = false)
            : base(shellManager.AppBarManager, shellManager.ExplorerHelper, shellManager.FullScreenHelper, screen, edge, deferRegistration ? AppBarMode.None : mode, 0)
        {
            _startupAppBarMode = mode;
            _registrationDeferred = deferRegistration;
            IsPrimaryEdge = isPrimaryEdge;
            _dictionaryManager = dictionaryManager;
            _shellManager = shellManager;
            _startMenuMonitor = startMenuMonitor;
            _updater = updater;
            this.windowManager = windowManager;
            this.hotkeyManager = hotkeyManager;

            InitializeComponent();
            DataContext = _shellManager;
            StartButton.StartMenuMonitor = startMenuMonitor;

            RecalculateSize(false);

            AllowsTransparency = mode == AppBarMode.AutoHide || (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);

            FlowDirection = Application.Current.FindResource("flow_direction") as FlowDirection? ?? FlowDirection.LeftToRight;

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            if (Settings.Instance.ShowQuickLaunch)
            {
                QuickLaunchToolbar.Visibility = Visibility.Visible;
            }

            if (Settings.Instance.ShowDesktopButton)
            {
                ShowDesktopButtonTray.Visibility = Visibility.Visible;
            }

            UpdateStartButton();
            UpdateTrayVisibility();
            UpdateClockVisibility();

            AutoHideElement = TaskbarContentControl;

            PropertyChanged += Taskbar_PropertyChanged;

            _startMenuMonitor.StartMenuVisibilityChanged += StartMenuMonitor_StartMenuVisibilityChanged;
            _shellManager.TasksService.WindowActivated += TasksService_WindowActivated;
        }

        internal void CompleteDeferredRegistration()
        {
            if (!_registrationDeferred)
            {
                return;
            }

            _registrationDeferred = false;
            AppBarMode = _startupAppBarMode;
        }

        internal int DesiredThicknessPixels => Convert.ToInt32(
            (Orientation == Orientation.Vertical ? DesiredWidth : DesiredHeight) * DpiScale);

        internal void CompleteDeferredStandaloneLayout(NativeMethods.Rect rect)
        {
            _registrationDeferred = false;
            _standaloneBounds = rect;
            SetWindowPosition(rect);
        }

        internal void SetStandaloneLayout(NativeMethods.Rect rect)
        {
            _standaloneBounds = rect;
            SetWindowPosition(rect);
        }

        public override bool UpdatePosition()
        {
            if (windowManager?.UsesManualWorkArea != true)
            {
                return base.UpdatePosition();
            }

            return _standaloneBounds.HasValue && SetWindowPosition(_standaloneBounds.Value);
        }

        private void TasksService_WindowActivated(object sender, ManagedShell.WindowsTasks.WindowEventArgs e)
        {
            TaskListControl?.QueueTaskRebuild();
            // If full-screen is suppressed, and a full-screen window is activated, it's time to un-suppress.

            if (!_fullScreenSuppressed)
            {
                return;
            }

            _fullScreenSuppressed = false;

            if (!HasFullScreenApp())
            {
                return;
            }

            for (int i = 0; i < _fullScreenHelper.FullScreenApps.Count; i++)
            {
                if (_fullScreenHelper.FullScreenApps[i].hWnd == e.Window.Handle)
                {
                    base.OnFullScreenEnter(_fullScreenHelper.FullScreenApps[i]);
                    return;
                }
            }
        }

        private void StartMenuMonitor_StartMenuVisibilityChanged(object sender, StartMenuMonitor.StartMenuMonitorEventArgs e)
        {
            if (!HasFullScreenApp() || !e.Visible)
            {
                return;
            }

            _fullScreenSuppressed = true;
            base.OnFullScreenLeave();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme))
            {
                bool newTransparency = AppBarMode == AppBarMode.AutoHide || (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);

                if (AllowsTransparency != newTransparency && Screen.Primary)
                {
                    // Transparency cannot be changed on an open window.
                    windowManager.ReopenTaskbars();
                    return;
                }

                SetBlur(AllowsBlur());
                PeekDuringAutoHide();
                RecalculateSize();
            }
            else if (e.PropertyName == nameof(Settings.ShowQuickLaunch))
            {
                if (Settings.Instance.ShowQuickLaunch)
                {
                    QuickLaunchToolbar.Visibility = Visibility.Visible;
                }
                else
                {
                    QuickLaunchToolbar.Visibility = Visibility.Collapsed;
                }
            }
            else if (e.PropertyName == nameof(Settings.Edge))
            {
                // Additional taskbars own their edge; only the primary one follows this setting.
                if (!IsPrimaryEdge)
                {
                    return;
                }

                PeekDuringAutoHide();
                AppBarEdge = Settings.Instance.Edge;
                UpdatePosition();
                UpdateTrayVisibility();
                UpdateClockVisibility();
                UpdateStartButton();
            }
            else if (e.PropertyName == nameof(Settings.TrayEdge))
            {
                UpdateTrayVisibility();
            }
            else if (e.PropertyName == nameof(Settings.ClockEdge))
            {
                UpdateClockVisibility();
            }
            else if (e.PropertyName == nameof(Settings.StartButtonEdge))
            {
                UpdateStartButton();
            }
            else if (e.PropertyName == nameof(Settings.Language))
            {
                FlowDirection newFlowDirection = Application.Current.FindResource("flow_direction") as FlowDirection? ?? FlowDirection.LeftToRight;

                if (FlowDirection != newFlowDirection && Screen.Primary)
                {
                    // It is necessary to reopen the taskbars to refresh menu sizes.
                    windowManager.ReopenTaskbars();
                    return;
                }
            }
            else if (e.PropertyName == nameof(Settings.ShowDesktopButton))
            {
                if (Settings.Instance.ShowDesktopButton)
                {
                    ShowDesktopButtonTray.Visibility = Visibility.Visible;
                }
                else
                {
                    ShowDesktopButtonTray.Visibility = Visibility.Collapsed;
                }
            }
            else if (e.PropertyName == nameof(Settings.TaskbarScale))
            {
                PeekDuringAutoHide();
                RecalculateSize();
                OnPropertyChanged(nameof(IsScaled));
            }
            else if (e.PropertyName == nameof(Settings.AutoHide))
            {
                bool newTransparency = Settings.Instance.AutoHide || (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);

                if (AllowsTransparency == newTransparency)
                {
                    AppBarMode = Settings.Instance.AutoHide ? AppBarMode.AutoHide : AppBarMode.Normal;
                }
                else if (Screen.Primary)
                {
                    // Auto hide requires transparency
                    // Transparency cannot be changed on an open window.
                    windowManager.ReopenTaskbars();
                }
            }
            else if (e.PropertyName == nameof(Settings.LockTaskbar))
            {
                OnPropertyChanged(nameof(IsLocked));
                PeekDuringAutoHide();
                RecalculateSize();
            }
            else if (e.PropertyName == nameof(Settings.RowCount) || e.PropertyName == nameof(Settings.TaskbarWidth) || e.PropertyName == nameof(Settings.EdgeSizes))
            {
                PeekDuringAutoHide();
                RecalculateSize();
                OnPropertyChanged(nameof(Rows));
            }
            else if (e.PropertyName == nameof(Settings.ShowStartButtonMultiMon))
            {
                UpdateStartButton();
            }
            else if (e.PropertyName == nameof(Settings.AutoHideTransparent))
            {
                PeekDuringAutoHide();
            }
            else if (e.PropertyName == nameof(Settings.AllowBlurBehind))
            {
                SetBlur(AllowsBlur());
            }
        }

        #region AppBarWindow overrides
        protected override void OnSourceInitialized(object sender, EventArgs e)
        {
            base.OnSourceInitialized(sender, e);

            SetLayoutRounding();
            SetBlur(AllowsBlur());
            UpdateTrayPosition();
        }
        
        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGING &&
                windowManager?.UsesManualWorkArea == true && _standaloneBounds.HasValue && !AllowClose)
            {
                var proposed = NativeMethods.WINDOWPOS.FromMessage(lParam);
                var expected = _standaloneBounds.Value;
                bool moving = (proposed.flags & NativeMethods.SetWindowPosFlags.SWP_NOMOVE) == 0;
                bool sizing = (proposed.flags & NativeMethods.SetWindowPosFlags.SWP_NOSIZE) == 0;
                if ((moving && (proposed.x != expected.Left || proposed.y != expected.Top)) ||
                    (sizing && (proposed.cx != expected.Width || proposed.cy != expected.Height)))
                {
                    proposed.x = expected.Left;
                    proposed.y = expected.Top;
                    proposed.cx = expected.Width;
                    proposed.cy = expected.Height;
                    proposed.flags &= ~(NativeMethods.SetWindowPosFlags.SWP_NOMOVE | NativeMethods.SetWindowPosFlags.SWP_NOSIZE);
                    proposed.UpdateMessage(lParam);
                }
            }

            base.WndProc(hwnd, msg, wParam, lParam, ref handled);

            if (msg == (int)NativeMethods.WM.SETTINGCHANGE && wParam == (IntPtr)NativeMethods.SPI.SETWORKAREA)
            {
                windowManager?.NotifyWorkAreaChange();
                return IntPtr.Zero;
            }

            if ((msg == (int)NativeMethods.WM.SYSCOLORCHANGE || 
                    msg == (int)NativeMethods.WM.SETTINGCHANGE) && 
                Settings.Instance.Theme.StartsWith(DictionaryManager.THEME_DEFAULT))
            {
                handled = true;

                // If the color scheme changes, re-apply the current theme to get updated colors.
                _dictionaryManager.SetThemeFromSettings();
            }
            return IntPtr.Zero;
        }

        protected override void CustomClosing()
        {
            if (AllowClose)
            {
                QuickLaunchToolbar.Visibility = Visibility.Collapsed;

                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
                _startMenuMonitor.StartMenuVisibilityChanged -= StartMenuMonitor_StartMenuVisibilityChanged;
                _shellManager.TasksService.WindowActivated -= TasksService_WindowActivated;
                StopElementDragHook();
            }
        }

        protected override void SetScreenProperties(ScreenSetupReason reason)
        {
            if (reason == ScreenSetupReason.DpiChange)
            {
                // DPI change is per-monitor, update ourselves
                if (windowManager?.UsesManualWorkArea == true)
                    windowManager.RefreshManualLayout();
                else
                    UpdatePosition();
                SetLayoutRounding();
                return;
            }

            if (windowManager?.UsesManualWorkArea == true)
            {
                windowManager.NotifyDisplayChange(reason);
                return;
            }

            if (Settings.Instance.ShowMultiMon)
            {
                // Re-create UltraWinBar windows based on new screen setup
                windowManager.NotifyDisplayChange(reason);
            }
            else
            {
                // Update window as necessary
                base.SetScreenProperties(reason);
            }
        }

        protected override bool ShouldAllowAutoHide()
        {
            return (!_startMenuOpen || !Screen.Primary) && _openMenus < 1 && base.ShouldAllowAutoHide();
        }

        protected override void OnAutoHideAnimationBegin(bool isHiding)
        {
            base.OnAutoHideAnimationBegin(isHiding);

            // Prevent focus indicators and tooltips while hidden
            ResetControlFocus();

            if (!isHiding && Opacity < 1)
            {
                Opacity = 1;
                OnPropertyChanged(nameof(Opacity));
            }
        }

        protected override void OnAutoHideAnimationComplete(bool isHiding)
        {
            base.OnAutoHideAnimationComplete(isHiding);

            if (isHiding && Settings.Instance.AutoHideTransparent && AllowsTransparency && AllowAutoHide)
            {
                Opacity = 0.01;
                OnPropertyChanged(nameof(Opacity));
            }
        }

        protected override void OnFullScreenEnter(FullScreenApp app)
        {
            base.OnFullScreenEnter(app);
            StartButton?.UpdateFloatingStartTopmost(false);
        }

        protected override void OnFullScreenLeave()
        {
            base.OnFullScreenLeave();
            StartButton?.UpdateFloatingStartTopmost(true);
        }
        #endregion

        #region Taskbar events
        private void Taskbar_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DpiScale))
            {
                OnPropertyChanged(nameof(IsScaled));
            }
        }

        private void Taskbar_OnLocationChanged(object sender, EventArgs e)
        {
            UpdateTrayPosition();
            StartButton?.UpdateFloatingStartCoordinates();
        }

        private void Taskbar_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTrayPosition();
            StartButton?.UpdateFloatingStartCoordinates();
        }

        private void Taskbar_Deactivated(object sender, EventArgs e)
        {
            if (AppBarMode != AppBarMode.AutoHide)
            {
                // Prevent focus indicators and tooltips while not the active window
                // When auto-hide is enabled, this is performed by auto-hide events instead
                ResetControlFocus();
            }
        }
        #endregion

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

        private void RecalculateSize(bool performResize = true)
        {
            _unlockedMargin = Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarUnlockedSize") as double? ?? 0);
            DesiredRowHeight = Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarRowHeight") as double? ?? 0);
            double newWidth = (Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarWidth") as double? ?? 0)) + DesiredRowHeight * (TaskbarWidthCount - 1);
            double newHeight = (Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarHeight") as double? ?? 0)) + DesiredRowHeight * (Rows - 1);

            AppBarMode effectiveMode = _registrationDeferred ? _startupAppBarMode : AppBarMode;
            if (effectiveMode == AppBarMode.AutoHide || !Settings.Instance.LockTaskbar)
            {
                newHeight += _unlockedMargin;
                newWidth += _unlockedMargin;
            }

            bool heightChanged = newHeight != DesiredHeight;
            bool widthChanged = newWidth != DesiredWidth;

            DesiredHeight = newHeight;
            DesiredWidth = newWidth;

            if (!performResize)
            {
                return;
            }

            if ((Orientation == Orientation.Horizontal && heightChanged) || (Orientation == Orientation.Vertical && widthChanged))
            {
                if (!windowManager.UsesManualWorkArea)
                {
                    UpdatePosition();
                }
            }
        }

        private void ResetControlFocus()
        {
            FocusDummyButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
        }

        private void SetLayoutRounding()
        {
            // Layout rounding causes incorrect sizing on non-integer scales
            if (DpiScale % 1 != 0)
            {
                UseLayoutRounding = false;
            }
            else
            {
                UseLayoutRounding = true;
            }
        }

        public void SetStartMenuOpen(bool isOpen)
        {
            bool currentAutoHide = AllowAutoHide;
            _startMenuOpen = isOpen;

            if (AllowAutoHide != currentAutoHide)
            {
                OnPropertyChanged(nameof(AllowAutoHide));
            }
        }

        public void SetTrayHost()
        {
            _shellManager.NotificationArea.SetTrayHostSizeData(new TrayHostSizeData
            {
                edge = (NativeMethods.ABEdge)AppBarEdge,
                rc = new NativeMethods.Rect
                {
                    Top = (int)(Top * DpiScale),
                    Left = (int)(Left * DpiScale),
                    Bottom = (int)((Top + Height) * DpiScale),
                    Right = (int)((Left + Width) * DpiScale)
                }
            });
        }

        public void AddOpenMenu()
        {
            bool currentAutoHide = AllowAutoHide;
            _openMenus++;

            if (AllowAutoHide != currentAutoHide)
            {
                OnPropertyChanged(nameof(AllowAutoHide));
            }
        }

        public void RemoveOpenMenu()
        {
            bool currentAutoHide = AllowAutoHide;
            _openMenus--;

            if (AllowAutoHide != currentAutoHide)
            {
                OnPropertyChanged(nameof(AllowAutoHide));
            }
        }

        private void UpdateTrayPosition()
        {
            if (Screen.Primary && HostsTray)
            {
                SetTrayHost();
            }
        }

        /// <summary>
        /// The notification area lives on exactly one taskbar per screen.
        /// </summary>
        public bool HostsTray => AppBarEdge == Settings.Instance.ResolvedTrayEdge;

        /// <summary>
        /// The clock lives on exactly one taskbar per screen, independent of the tray.
        /// </summary>
        public bool HostsClock => AppBarEdge == Settings.Instance.ResolvedClockEdge;

        /// <summary>
        /// The start button lives on exactly one taskbar per screen.
        /// </summary>
        public bool HostsStartButton => AppBarEdge == Settings.Instance.ResolvedStartButtonEdge;

        private void UpdateClockVisibility()
        {
            ClockGroupBox.Visibility = HostsClock ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTrayVisibility()
        {
            TrayGroupBox.Visibility = HostsTray ? Visibility.Visible : Visibility.Collapsed;

            if (HostsTray)
            {
                UpdateTrayPosition();
            }
        }

        private void UpdateStartButton()
        {
            if (!HostsStartButton)
            {
                StartButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (!Screen.Primary && !Settings.Instance.ShowStartButtonMultiMon)
            {
                StartButton.Visibility = Visibility.Collapsed;
                return;
            }

            StartButton.Visibility = Visibility.Visible;
        }

        private bool HasFullScreenApp()
        {
            bool hasFullScreenApp = false;

            foreach (var app in _fullScreenHelper.FullScreenApps)
            {
                if (app.screen.DeviceName == Screen.DeviceName || app.screen.IsVirtualScreen)
                {
                    hasFullScreenApp = true;
                    break;
                }
            }

            return hasFullScreenApp;
        }

        private bool AllowsBlur()
        {
            return Settings.Instance.AllowBlurBehind &&
                   (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);
        }


        #region Clock/tray drag between taskbars
        private LowLevelMouseHook _elementDragHook;
        private LowLevelMouseHook.POINT _elementDragStartScreenPos;
        private bool _isDraggingElement;
        private Action<AppBarEdge> _elementDragTarget;
        private string _elementDragGlyph;

        private void ClockGroupBox_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartElementDragHook(edge => Settings.Instance.ClockEdge = edge, "◷");
        }

        private void TrayGroupBox_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartElementDragHook(edge => Settings.Instance.TrayEdge = edge, "▦");
        }

        private void InputLanguageGroup_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartElementDragHook(edge => Settings.Instance.LanguageEdge = edge, "A");
        }

        // Observe the threshold without consuming ordinary clicks.
        private void StartElementDragHook(Action<AppBarEdge> onDroppedOnEdge, string glyph)
        {
            if (Settings.Instance.EnabledEdges.Count < 2 || _elementDragHook != null)
            {
                return;
            }

            _elementDragTarget = onDroppedOnEdge;
            ShellLogger.Debug($"Panel element drag: armed on {AppBarEdge}, glyph={glyph}");
            _elementDragGlyph = glyph;
            _elementDragStartScreenPos = new LowLevelMouseHook.POINT
            {
                X = System.Windows.Forms.Cursor.Position.X,
                Y = System.Windows.Forms.Cursor.Position.Y
            };
            _isDraggingElement = false;

            _elementDragHook = new LowLevelMouseHook();
            _elementDragHook.LowLevelMouseEvent += ElementDragHook_LowLevelMouseEvent;
            if (!_elementDragHook.Initialize()) StopElementDragHook();
        }

        private void ElementDragHook_LowLevelMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            switch (e.Message)
            {
                case NativeMethods.WM.MOUSEMOVE:
                    if (!_isDraggingElement)
                    {
                        if (Math.Abs(e.HookStruct.pt.X - _elementDragStartScreenPos.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                            Math.Abs(e.HookStruct.pt.Y - _elementDragStartScreenPos.Y) <= SystemParameters.MinimumVerticalDragDistance)
                        {
                            return;
                        }

                        _isDraggingElement = true;
                        var apply = _elementDragTarget;
                        var glyph = _elementDragGlyph;
                        StopElementDragHook();
                        Dispatcher.BeginInvoke(() =>
                        {
                            ShellLogger.Debug($"Panel element drag: threshold reached, buttons={System.Windows.Forms.Control.MouseButtons}");
                            if ((System.Windows.Forms.Control.MouseButtons & System.Windows.Forms.MouseButtons.Left) != 0 && IsVisible)
                                PanelElementDrag.Run(this, glyph, apply);
                        });
                    }
                    break;
                case NativeMethods.WM.LBUTTONUP:
                case NativeMethods.WM.RBUTTONUP:
                case NativeMethods.WM.MBUTTONUP:
                case NativeMethods.WM.XBUTTONUP:
                    StopElementDragHook();
                    break;
            }
        }

        private void StopElementDragHook()
        {
            if (_elementDragHook == null)
            {
                return;
            }

            _elementDragHook.LowLevelMouseEvent -= ElementDragHook_LowLevelMouseEvent;
            _elementDragHook.Dispose();
            _elementDragHook = null;
            _isDraggingElement = false;
            _elementDragTarget = null;
        }
        #endregion
    }
}
