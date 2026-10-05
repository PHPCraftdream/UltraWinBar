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
        private IDisposable _mouseDragHookSubscription;
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
        private bool _reserved;
        
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

            WeakSubscriptions.SubscribeSettings(Settings_PropertyChanged);

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
            _reserved = PanelReservation.Reserve(Handle, AppBarEdge, rect, _reserved);
        }

        internal void SetStandaloneLayout(NativeMethods.Rect rect)
        {
            _standaloneBounds = rect;
            SetWindowPosition(rect);
            _reserved = PanelReservation.Reserve(Handle, AppBarEdge, rect, _reserved);
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
            // A closed panel still receives the change that closed it.
            if (IsClosing || AllowClose) return;

            if (e.PropertyName == nameof(Settings.Theme))
            {
                bool newTransparency = AppBarMode == AppBarMode.AutoHide || (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);

                if (AllowsTransparency != newTransparency && Screen.Primary)
                {
                    // Transparency cannot be changed on an open window.
                    windowManager.RequestReopenTaskbars();
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
                    windowManager.RequestReopenTaskbars();
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
                    windowManager.RequestReopenTaskbars();
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

    }
}
