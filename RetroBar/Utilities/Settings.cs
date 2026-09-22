using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTray;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RetroBar.Utilities
{
    internal class Settings : INotifyPropertyChanged, IMigratableSettings
    {
        private static Settings instance;

        public static Settings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = _settingsManager.Settings;
                    _isInitializing = false;
                }

                return instance;
            }
        }

        private static string _settingsPath = "settings.json".InLocalAppData();
        private static bool _isInitializing = true;
        private static SettingsManager<Settings> _settingsManager = new(_settingsPath, new Settings());

        private bool _migrationPerformed = false;
        public bool MigrationPerformed { get => _migrationPerformed; }
        public event PropertyChangedEventHandler PropertyChanged;

        // This should not be used directly! Unfortunately it must be public for JsonSerializer.
        public Settings()
        {
            PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _settingsManager.Settings = this;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (!field.Equals(value))
            {
                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        protected void SetEnum<T>(ref T field, T value, [CallerMemberName] string propertyName = "") where T : Enum
        {
            if (!field.Equals(value))
            {
                if (!Enum.IsDefined(typeof(T), value))
                {
                    return;
                }

                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        #region Properties
        private string _language = "System";
        public string Language
        {
            get => _language;
            set => Set(ref _language, value);
        }

        private string _theme = "Windows 95-98";
        public string Theme
        {
            get => _theme;
            set => Set(ref _theme, value);
        }

        private bool _showInputLanguage = false;
        public bool ShowInputLanguage
        {
            get => _showInputLanguage;
            set => Set(ref _showInputLanguage, value);
        }

        private bool _showClock = true;
        public bool ShowClock
        {
            get => _showClock;
            set => Set(ref _showClock, value);
        }

        private bool _overrideClockFormat = false;
        public bool OverrideClockFormat
        {
            get
            {
                return _overrideClockFormat;
            }
            set
            {
                if (_overrideClockFormat != value)
                {
                    _overrideClockFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _clockFormat = "ddd MMM d  h:mm:ss tt";
        public string ClockFormat
        {
            get
            {
                return _clockFormat;
            }
            set
            {
                if (_clockFormat != value)
                {
                    _clockFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _overrideAMPMDesignators = false;
        public bool OverrideAMPMDesignators
        {
            get
            {
                return _overrideAMPMDesignators;
            }
            set
            {
                if (_overrideAMPMDesignators != value)
                {
                    _overrideAMPMDesignators = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _amDesignator = "a.m.";
        public string AMDesignator
        {
            get
            {
                return _amDesignator;
            }
            set
            {
                if (_amDesignator != value)
                {
                    _amDesignator = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _pmDesignator = "p.m.";
        public string PMDesignator
        {
            get
            {
                return _pmDesignator;
            }
            set
            {
                if (_pmDesignator != value)
                {
                    _pmDesignator = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _showDesktopButton = false;
        public bool ShowDesktopButton
        {
            get => _showDesktopButton;
            set => Set(ref _showDesktopButton, value);
        }

        private bool _peekAtDesktop = false;
        public bool PeekAtDesktop
        {
            get => _peekAtDesktop;
            set => Set(ref _peekAtDesktop, value);
        }

        private bool _showMultiMon = false;
        public bool ShowMultiMon
        {
            get => _showMultiMon;
            set => Set(ref _showMultiMon, value);
        }

        private bool _showQuickLaunch = true;
        public bool ShowQuickLaunch
        {
            get => _showQuickLaunch;
            set => Set(ref _showQuickLaunch, value);
        }

        private string _quickLaunchPath = "%appdata%\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar";
        public string QuickLaunchPath
        {
            get => _quickLaunchPath;
            set => Set(ref _quickLaunchPath, value);
        }

        private bool _collapseNotifyIcons = false;
        public bool CollapseNotifyIcons
        {
            get => _collapseNotifyIcons;
            set => Set(ref _collapseNotifyIcons, value);
        }

        private List<string> _invertNotifyIcons = new List<string> { NotificationArea.HARDWARE_GUID, NotificationArea.UPDATE_GUID, NotificationArea.MICROPHONE_GUID, NotificationArea.LOCATION_GUID, NotificationArea.MEETNOW_GUID, NotificationArea.NETWORK_GUID, NotificationArea.POWER_GUID, NotificationArea.VOLUME_GUID };
        public List<string> InvertNotifyIcons
        {
            get => _invertNotifyIcons;
            set => Set(ref _invertNotifyIcons, value);
        }

        private List<NotifyIconBehaviorSetting> _notifyIconBehaviors = new List<NotifyIconBehaviorSetting>
        {
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.HEALTH_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.POWER_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.NETWORK_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.VOLUME_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
        };
        public List<NotifyIconBehaviorSetting> NotifyIconBehaviors
        {
            get => _notifyIconBehaviors;
            set => Set(ref _notifyIconBehaviors, value);
        }

        private List<string> _notifyIconOrder = new List<string>();
        public List<string> NotifyIconOrder
        {
            get => _notifyIconOrder;
            set => Set(ref _notifyIconOrder, value);
        }

        private bool _allowFontSmoothing = false;
        public bool AllowFontSmoothing
        {
            get => _allowFontSmoothing;
            set => Set(ref _allowFontSmoothing, value);
        }

        private bool _allowFontSmoothingMenu = true;
        public bool AllowFontSmoothingMenu
        {
            get => _allowFontSmoothingMenu;
            set => Set(ref _allowFontSmoothingMenu, value);
        }

        private bool _useSoftwareRendering = false;
        public bool UseSoftwareRendering
        {
            get => _useSoftwareRendering;
            set => Set(ref _useSoftwareRendering, value);
        }

        private AppBarEdge _edge = AppBarEdge.Bottom;
        public AppBarEdge Edge
        {
            get => _edge;
            set
            {
                SetEnum(ref _edge, value);
                // The primary edge changed identity, so the size it resolves to may too.
                OnPropertyChanged(nameof(PrimaryRowCount));
                OnPropertyChanged(nameof(PrimaryTaskbarWidth));
            }
        }

        private int _rowCount = 1;
        public int RowCount
        {
            get => _rowCount;
            set => Set(ref _rowCount, value);
        }

        private int _rowLimit = 5;
        public int RowLimit
        {
            get => _rowLimit;
            set => Set(ref _rowLimit, value);
        }

        private int _taskbarWidth = 1;
        public int TaskbarWidth
        {
            get => _taskbarWidth;
            set => Set(ref _taskbarWidth, value);
        }

        private int _taskbarWidthLimit = 7;
        public int TaskbarWidthLimit
        {
            get => _taskbarWidthLimit;
            set => Set(ref _taskbarWidthLimit, value);
        }

        private List<string> _quickLaunchOrder = [];
        public List<string> QuickLaunchOrder
        {
            get => _quickLaunchOrder;
            set => Set(ref _quickLaunchOrder, value);
        }

        private bool _showTaskThumbnails = false;
        public bool ShowTaskThumbnails
        {
            get => _showTaskThumbnails;
            set => Set(ref _showTaskThumbnails, value);
        }

        private MultiMonOption _multiMonMode = MultiMonOption.AllTaskbars;
        public MultiMonOption MultiMonMode
        {
            get => _multiMonMode;
            set => SetEnum(ref _multiMonMode, value);
        }

        private double _taskbarScale = 1.0;
        public double TaskbarScale
        {
            get => _taskbarScale;
            set => Set(ref _taskbarScale, value);
        }

        private bool _debugLogging = false;
        public bool DebugLogging
        {
            get => _debugLogging;
            set => Set(ref _debugLogging, value);
        }

        private bool _autoHide = false;
        public bool AutoHide
        {
            get => _autoHide;
            set => Set(ref _autoHide, value);
        }

        private bool _lockTaskbar = false;
        public bool LockTaskbar
        {
            get => _lockTaskbar;
            set => Set(ref _lockTaskbar, value);
        }

        private InvertIconsOption _invertIconsMode = EnvironmentHelper.IsWindows10OrBetter ? InvertIconsOption.WhenNeededByTheme : InvertIconsOption.Never;
        public InvertIconsOption InvertIconsMode
        {
            get => _invertIconsMode;
            set => SetEnum(ref _invertIconsMode, value);
        }

        private bool _showTaskBadges = true;
        public bool ShowTaskBadges
        {
            get => _showTaskBadges;
            set => Set(ref _showTaskBadges, value);
        }

        private TaskMiddleClickOption _taskMiddleClickAction = TaskMiddleClickOption.OpenNewInstance;
        public TaskMiddleClickOption TaskMiddleClickAction
        {
            get => _taskMiddleClickAction;
            set => SetEnum(ref _taskMiddleClickAction, value);
        }

        private TaskWheelActionOption _taskWheelAction = TaskWheelActionOption.DoNothing;
        public TaskWheelActionOption TaskWheelAction
        {
            get => _taskWheelAction;
            set => SetEnum(ref _taskWheelAction, value);
        }

        private ClockClickOption _clockClickAction = EnvironmentHelper.IsWindows10OrBetter ? ClockClickOption.OpenNotificationCenter : ClockClickOption.DoNothing;
        public ClockClickOption ClockClickAction
        {
            get
            {
                // On Windows versions prior to 10, neither the Modern calendar nor the Notification Center is available
                if (!EnvironmentHelper.IsWindows10OrBetter && _clockClickAction > ClockClickOption.OpenAeroCalendar)
                {
                    return ClockClickOption.DoNothing;
                }

                return _clockClickAction;
            }
            set => SetEnum(ref _clockClickAction, value);
        }

        private bool _checkForUpdates = true;
        public bool CheckForUpdates
        {
            get => _checkForUpdates;
            set => Set(ref _checkForUpdates, value);
        }

        private bool _showExitMenuItem = true;
        public bool ShowExitMenuItem
        {
            get => _showExitMenuItem;
            set => Set(ref _showExitMenuItem, value);
        }

        private bool _showEndTaskButton = false;
        public bool ShowEndTaskButton
        {
            get => _showEndTaskButton;
            set => Set(ref _showEndTaskButton, value);
        }

        private bool _showStartButtonMultiMon = false;
        public bool ShowStartButtonMultiMon
        {
            get => _showStartButtonMultiMon;
            set => Set(ref _showStartButtonMultiMon, value);
        }

        private bool _autoHideTransparent = false;
        public bool AutoHideTransparent
        {
            get => _autoHideTransparent;
            set => Set(ref _autoHideTransparent, value);
        }

        private bool _slideTaskbarButtons = false;
        public bool SlideTaskbarButtons
        {
            get => _slideTaskbarButtons;
            set => Set(ref _slideTaskbarButtons, value);
        }

        private bool _showClockSeconds = false;
        public bool ShowClockSeconds
        {
            get => _showClockSeconds;
            set => Set(ref _showClockSeconds, value);
        }

        private WinNumHotkeysOption _winNumHotkeysAction = WinNumHotkeysOption.WindowsDefault;
        public WinNumHotkeysOption WinNumHotkeysAction
        {
            get => _winNumHotkeysAction;
            set => SetEnum(ref _winNumHotkeysAction, value);
        }

        private bool _allowBlurBehind = true;
        public bool AllowBlurBehind
        {
            get => _allowBlurBehind;
            set => Set(ref _allowBlurBehind, value);
        }

        // Extra taskbars beyond the primary one on Edge. Empty by default, so
        // existing setups keep their single taskbar.
        private List<AppBarEdge> _additionalEdges = [];
        public List<AppBarEdge> AdditionalEdges
        {
            get => _additionalEdges;
            set => Set(ref _additionalEdges, value);
        }

        // Taskbar hosting the notification area. There is only ever one.
        private AppBarEdge _trayEdge = AppBarEdge.Bottom;
        public AppBarEdge TrayEdge
        {
            get => _trayEdge;
            set => SetEnum(ref _trayEdge, value);
        }

        // Taskbar hosting the clock. There is only ever one, independent of the tray.
        private AppBarEdge _clockEdge = AppBarEdge.Bottom;
        public AppBarEdge ClockEdge
        {
            get => _clockEdge;
            set => SetEnum(ref _clockEdge, value);
        }

        // Taskbar hosting the start button. There is only ever one.
        private AppBarEdge _startButtonEdge = AppBarEdge.Bottom;
        public AppBarEdge StartButtonEdge
        {
            get => _startButtonEdge;
            set => SetEnum(ref _startButtonEdge, value);
        }

        // Taskbar hosting the input language indicator. There is only ever one.
        private AppBarEdge _languageEdge = AppBarEdge.Bottom;
        public AppBarEdge LanguageEdge
        {
            get => _languageEdge;
            set => SetEnum(ref _languageEdge, value);
        }

        // Taskbar new, unpinned windows open on by default ("Make main" on a taskbar's
        // context menu sets this). Independent of Edge, which is the primary taskbar's
        // physical location, not where unassigned windows land.
        private AppBarEdge _defaultTaskEdge = AppBarEdge.Bottom;
        public AppBarEdge DefaultTaskEdge
        {
            get => _defaultTaskEdge;
            set => SetEnum(ref _defaultTaskEdge, value);
        }

        // Which taskbar each window/application is pinned to, once dragged there by the
        // user (plain drag = that window only, Ctrl+drag = the whole application).
        private List<TaskbarAssignment> _taskbarAssignments = [];
        public List<TaskbarAssignment> TaskbarAssignments
        {
            get => _taskbarAssignments;
            set => Set(ref _taskbarAssignments, value);
        }

        // Per-edge task button order, keyed by TaskAssignmentManager's WindowClassAndTitle
        // identifier. Relative position among entries sharing an Edge defines that edge's
        // order; entries for other edges may be interleaved.
        private List<TaskOrderEntry> _taskOrder = [];
        public List<TaskOrderEntry> TaskOrder
        {
            get => _taskOrder;
            set => Set(ref _taskOrder, value);
        }

        // Which taskbar each Quick Launch shortcut is shown on. Order within an edge is
        // still governed by QuickLaunchOrder below; this only controls edge membership.
        private List<QuickLaunchAssignment> _quickLaunchAssignments = [];
        public List<QuickLaunchAssignment> QuickLaunchAssignments
        {
            get => _quickLaunchAssignments;
            set => Set(ref _quickLaunchAssignments, value);
        }

        // Edges the user has explicitly "stretched", most-recent first. An edge here
        // registers with the OS AppBar API before edges not listed, so it keeps its full
        // length and neighboring taskbars shrink to make room for it instead.
        private List<AppBarEdge> _edgePriority = [];
        public List<AppBarEdge> EdgePriority
        {
            get => _edgePriority;
            set => Set(ref _edgePriority, value);
        }

        // Per-edge taskbar size override. Each edge is either horizontal or vertical, never
        // both, so one Size field covers RowCount (horizontal) or TaskbarWidth (vertical).
        // Edges without an entry fall back to the global RowCount/TaskbarWidth default below.
        private List<EdgeSizeSetting> _edgeSizes = [];
        public List<EdgeSizeSetting> EdgeSizes
        {
            get => _edgeSizes;
            set => Set(ref _edgeSizes, value);
        }
        #endregion

        #region Computed helpers
        // Read-only, so these are not written to settings.json.

        /// <summary>
        /// Every edge that should have a taskbar: the primary one plus any extras.
        /// </summary>
        // System.Text.Json serializes read-only collection properties regardless of
        // IgnoreReadOnlyProperties, so this needs an explicit opt-out.
        [JsonIgnore]
        public List<AppBarEdge> EnabledEdges
        {
            get
            {
                List<AppBarEdge> edges = [Edge];

                foreach (AppBarEdge edge in AdditionalEdges)
                {
                    if (!edges.Contains(edge))
                    {
                        edges.Add(edge);
                    }
                }

                return edges;
            }
        }

        /// <summary>
        /// TrayEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedTrayEdge => EnabledEdges.Contains(TrayEdge) ? TrayEdge : Edge;

        /// <summary>
        /// ClockEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedClockEdge => EnabledEdges.Contains(ClockEdge) ? ClockEdge : Edge;

        /// <summary>
        /// StartButtonEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedStartButtonEdge => EnabledEdges.Contains(StartButtonEdge) ? StartButtonEdge : Edge;

        /// <summary>
        /// LanguageEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedLanguageEdge => EnabledEdges.Contains(LanguageEdge) ? LanguageEdge : Edge;

        /// <summary>
        /// DefaultTaskEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedDefaultTaskEdge => EnabledEdges.Contains(DefaultTaskEdge) ? DefaultTaskEdge : Edge;

        /// <summary>
        /// The order taskbars should register with the OS AppBar API on each screen.
        /// Edges earlier in this list keep their full length; later ones get shrunk by
        /// the OS to avoid overlapping earlier ones. Defaults to vertical edges first
        /// (Left, Right), since a full-height side bar reads as more "primary" than a
        /// strip across the top/bottom, then falls back to Top, Bottom.
        /// </summary>
        [JsonIgnore]
        public List<AppBarEdge> ResolvedEdgeOrder
        {
            get
            {
                List<AppBarEdge> enabled = EnabledEdges;
                List<AppBarEdge> order = [];

                foreach (AppBarEdge edge in EdgePriority)
                {
                    if (enabled.Contains(edge) && !order.Contains(edge))
                    {
                        order.Add(edge);
                    }
                }

                foreach (AppBarEdge edge in new[] { AppBarEdge.Left, AppBarEdge.Right, AppBarEdge.Top, AppBarEdge.Bottom })
                {
                    if (enabled.Contains(edge) && !order.Contains(edge))
                    {
                        order.Add(edge);
                    }
                }

                return order;
            }
        }

        /// <summary>
        /// Per-edge taskbar size (row count or column count, depending on orientation),
        /// falling back to the given default if that edge has no override yet.
        /// </summary>
        public int GetEdgeSize(AppBarEdge edge, int defaultValue)
        {
            foreach (EdgeSizeSetting entry in EdgeSizes)
            {
                if (entry.Edge == edge)
                {
                    return entry.Size;
                }
            }

            return defaultValue;
        }

        public void SetEdgeSize(AppBarEdge edge, int value)
        {
            List<EdgeSizeSetting> edges = new List<EdgeSizeSetting>(EdgeSizes);
            int index = edges.FindIndex(entry => entry.Edge == edge);

            if (index >= 0)
            {
                EdgeSizeSetting entry = edges[index];
                entry.Size = value;
                edges[index] = entry;
            }
            else
            {
                edges.Add(new EdgeSizeSetting { Edge = edge, Size = value });
            }

            EdgeSizes = edges;
            OnPropertyChanged(nameof(PrimaryRowCount));
            OnPropertyChanged(nameof(PrimaryTaskbarWidth));
        }

        /// <summary>
        /// The saved task button order for one edge, as a list of identifiers.
        /// </summary>
        public List<string> GetTaskOrderForEdge(AppBarEdge edge)
        {
            List<string> result = new List<string>();

            foreach (TaskOrderEntry entry in TaskOrder)
            {
                if (entry.Edge == edge)
                {
                    result.Add(entry.Identifier);
                }
            }

            return result;
        }

        /// <summary>
        /// Replaces the saved order for one edge, preserving other edges' entries untouched.
        /// </summary>
        public void SetTaskOrderForEdge(AppBarEdge edge, List<string> identifiers)
        {
            List<TaskOrderEntry> entries = new List<TaskOrderEntry>();

            foreach (TaskOrderEntry entry in TaskOrder)
            {
                if (entry.Edge != edge)
                {
                    entries.Add(entry);
                }
            }

            foreach (string identifier in identifiers)
            {
                entries.Add(new TaskOrderEntry { Edge = edge, Identifier = identifier });
            }

            TaskOrder = entries;
        }

        /// <summary>
        /// Which taskbar a Quick Launch shortcut is shown on, falling back to the "main"
        /// taskbar (see DefaultTaskEdge / "Make main") if it hasn't been moved yet.
        /// </summary>
        public AppBarEdge GetQuickLaunchEdge(string path)
        {
            foreach (QuickLaunchAssignment assignment in QuickLaunchAssignments)
            {
                if (assignment.Path == path)
                {
                    return EnabledEdges.Contains(assignment.Edge) ? assignment.Edge : ResolvedDefaultTaskEdge;
                }
            }

            return ResolvedDefaultTaskEdge;
        }

        public void SetQuickLaunchEdge(string path, AppBarEdge edge)
        {
            List<QuickLaunchAssignment> assignments = new List<QuickLaunchAssignment>(QuickLaunchAssignments);
            int index = assignments.FindIndex(assignment => assignment.Path == path);

            if (index >= 0)
            {
                QuickLaunchAssignment assignment = assignments[index];
                assignment.Edge = edge;
                assignments[index] = assignment;
            }
            else
            {
                assignments.Add(new QuickLaunchAssignment { Path = path, Edge = edge });
            }

            QuickLaunchAssignments = assignments;
        }

        /// <summary>
        /// The primary edge's own row count (Properties dialog binds to this, not the
        /// global RowCount default, so editing it never affects other edges' panels).
        /// </summary>
        public int PrimaryRowCount
        {
            get => GetEdgeSize(Edge, RowCount);
            set => SetEdgeSize(Edge, value);
        }

        /// <summary>
        /// The primary edge's own width. See <see cref="PrimaryRowCount"/>.
        /// </summary>
        public int PrimaryTaskbarWidth
        {
            get => GetEdgeSize(Edge, TaskbarWidth);
            set => SetEdgeSize(Edge, value);
        }
        #endregion

        #region Old Properties
        public bool? MiddleMouseToClose
        {
            get => null;
            set
            {
                // Migrate to TaskMiddleClickAction
                if (value != null)
                {
                    TaskMiddleClickAction = (bool)value ? TaskMiddleClickOption.CloseTask : TaskMiddleClickOption.OpenNewInstance;
                    _migrationPerformed = true;
                }
            }
        }

        public string[] PinnedNotifyIcons
        {
            get => [];
            set
            {
                // Migrate to NotifyIconBehaviors
                if (value.Length > 0)
                {
                    var newSettings = new List<NotifyIconBehaviorSetting>();

                    foreach (var identifier in value)
                    {
                        newSettings.Add(new NotifyIconBehaviorSetting
                        {
                            Identifier = identifier,
                            Behavior = NotifyIconBehavior.AlwaysShow
                        });
                    }

                    NotifyIconBehaviors = newSettings;
                    _migrationPerformed = true;
                }
            }
        }
        #endregion
    }

    #region Enums
    public enum InvertIconsOption
    {
        WhenNeededByTheme,
        Always,
        Never
    }

    public enum MultiMonOption
    {
        AllTaskbars,
        SameAsWindow,
        SameAsWindowAndPrimary
    }

    public enum TaskMiddleClickOption
    {
        DoNothing,
        OpenNewInstance,
        CloseTask
    }
    public enum TaskWheelActionOption
    {
        DoNothing,
        ShowHideWindow
    }

    public enum ClockClickOption
    {
        DoNothing,
        OpenAeroCalendar,
        OpenModernCalendar,
        OpenNotificationCenter,
    }

    public enum WinNumHotkeysOption
    {
        WindowsDefault,
        SwitchTasks,
        InvokeQuickLaunch,
    }

    public enum NotifyIconBehavior
    {
        HideWhenInactive,
        AlwaysHide,
        AlwaysShow,
        Remove
    }

    /// <summary>
    /// How a window is identified for a taskbar assignment: chosen per drag by whether
    /// Ctrl is held, not a global setting.
    /// </summary>
    public enum TaskAssignmentMode
    {
        /// <summary>
        /// Group by executable (Ctrl+drag), so every window of an application shares one taskbar.
        /// </summary>
        ExecutablePath,

        /// <summary>
        /// Group by window class and title (plain drag), moving only that one window.
        /// </summary>
        WindowClassAndTitle
    }
    #endregion

    #region Structs
    public struct NotifyIconBehaviorSetting
    {
        public string Identifier {  get; set; }
        public NotifyIconBehavior Behavior { get; set; }
    }

    public struct TaskbarAssignment
    {
        public string Identifier { get; set; }
        public AppBarEdge Edge { get; set; }
        public TaskAssignmentMode Mode { get; set; }
    }

    public struct EdgeSizeSetting
    {
        public AppBarEdge Edge { get; set; }
        public int Size { get; set; }
    }

    public struct QuickLaunchAssignment
    {
        public string Path { get; set; }
        public AppBarEdge Edge { get; set; }
    }

    public struct TaskOrderEntry
    {
        public AppBarEdge Edge { get; set; }
        public string Identifier { get; set; }
    }
    #endregion
}