using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTray;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace UltraWinBar.Utilities
{
    internal partial class Settings : INotifyPropertyChanged, IMigratableSettings
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

        internal static void Flush()
        {
            _settingsManager.Flush();
        }

        // The test runner points this at a scratch file so it can never touch the user's settings.
        private static string _settingsPath = Environment.GetEnvironmentVariable("ULTRAWINBAR_SETTINGS_PATH") is { Length: > 0 } overridePath
            ? overridePath : "UltraWinBar.settings.json".InLocalAppData();
        private static bool _isInitializing = true;
        private static SettingsManager<Settings> _settingsManager = new(_settingsPath, new Settings());

        private bool _migrationPerformed = false;
        public bool MigrationPerformed { get => _migrationPerformed; }
        public event PropertyChangedEventHandler PropertyChanged;

        // Health diagnostics: a steadily growing count means a leaked subscriber.
        internal int PropertyChangedSubscriberCount => PropertyChanged?.GetInvocationList().Length ?? 0;

        // This should not be used directly! Unfortunately it must be public for JsonSerializer.
        public Settings()
        {
            PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Only the live instance persists: any other Settings object (defaults, a stray instance)
            // must never replace the user's settings file.
            if (_isInitializing || !ReferenceEquals(this, instance))
            {
                return;
            }

            _settingsManager.Settings = this;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            InvalidateEdgeCaches(propertyName);
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

        private string _theme = "Modern Glass Light";
        public string Theme
        {
            get => _theme;
            set => Set(ref _theme, value);
        }

        // The neon accent of the Modern Glass theme: one of ModernTheme.Accents.
        private string _accentColor = "cyan";
        public string AccentColor
        {
            get => _accentColor;
            set => Set(ref _accentColor, ModernTheme.IsAccent(value) ? value : "cyan");
        }

        private bool _showInputLanguage = true;
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

        private bool _showQuickLaunch = false;
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

        private List<string> _invertNotifyIcons = new List<string> { NotificationArea.HARDWARE_GUID, NotificationArea.UPDATE_GUID, NotificationArea.MICROPHONE_GUID, NotificationArea.LOCATION_GUID, NotificationArea.MEETNOW_GUID };
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

        private bool _allowFontSmoothing = true;
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

        private AppBarEdge _edge = AppBarEdge.Right;
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

        private int _taskbarWidth = 2;
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

        private double _taskbarScale = 1.2;
        public double TaskbarScale
        {
            get => _taskbarScale;
            set => Set(ref _taskbarScale, value);
        }

        private double _trayIconSize = 20;
        public double TrayIconSize
        {
            get => _trayIconSize;
            set => Set(ref _trayIconSize, double.IsNaN(value) ? 20 : Math.Clamp(value, 16, 32));
        }

        private bool _debugLogging = false;
        public bool DebugLogging
        {
            get => _debugLogging;
            set => Set(ref _debugLogging, value);
        }

        private bool _windowActivityLogging = false;
        public bool WindowActivityLogging
        {
            get => _windowActivityLogging;
            set => Set(ref _windowActivityLogging, value);
        }

        private List<string> _debugLogMasks =
        [
            "*",
            "!NotificationArea:*",
            "!TrayService: Handled AppBar message*"
        ];
        public List<string> DebugLogMasks
        {
            get => _debugLogMasks;
            set => Set(ref _debugLogMasks, value ?? []);
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

        private bool _showTaskBadges = false;
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

        private ClockClickOption _clockClickAction = ClockClickOption.OpenAeroCalendar;
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

        private bool _checkForUpdates = false;
        private bool _showHebrewDate;
        public bool ShowHebrewDate
        {
            get => _showHebrewDate;
            set => Set(ref _showHebrewDate, value);
        }

        // Where the Hebrew date is kept for (a city or typed-in coordinates); none until the user chooses.
        private HebrewPlace _hebrewPlace;
        public HebrewPlace HebrewPlace
        {
            get => _hebrewPlace;
            set
            {
                if (!Equals(_hebrewPlace, value))
                {
                    _hebrewPlace = value;
                    OnPropertyChanged();
                }
            }
        }

        // The work area of the panels' last layout and the screen it was for ("148,34,1772,1046;0,0,1920,1080"): put back at
        // the start of the next run, so that the windows stay where they were while the panels start.
        private string _lastWorkArea = "";
        public string LastWorkArea
        {
            get => _lastWorkArea;
            set => Set(ref _lastWorkArea, value ?? "");
        }

        // The work area as it was before the panels took their strips, with its screen: kept, because a restart that leaves the
        // panels' own work area in place would otherwise take that for the original.
        private string _originalWorkArea = "";
        public string OriginalWorkArea
        {
            get => _originalWorkArea;
            set => Set(ref _originalWorkArea, value ?? "");
        }

        // The place of the second and of the third time zone (the place of the main one is HebrewPlace): the Hebrew date, the
        // change of the day and the times of the day of the calendar are those of this place, on the clock of its zone.
        private HebrewPlace _secondPlace;
        public HebrewPlace SecondPlace
        {
            get => _secondPlace;
            set
            {
                if (!Equals(_secondPlace, value))
                {
                    _secondPlace = value;
                    OnPropertyChanged();
                }
            }
        }

        private HebrewPlace _thirdPlace;
        public HebrewPlace ThirdPlace
        {
            get => _thirdPlace;
            set
            {
                if (!Equals(_thirdPlace, value))
                {
                    _thirdPlace = value;
                    OnPropertyChanged();
                }
            }
        }

        // The time zone (IANA id, "Asia/Jerusalem") of a second time shown under the clock's own; empty shows none.
        private string _secondTimeZone = "";
        public string SecondTimeZone
        {
            get => _secondTimeZone;
            set => Set(ref _secondTimeZone, value ?? "");
        }

        // The main time zone of the clock (IANA id); empty is the computer's own.
        private string _mainTimeZone = "";
        public string MainTimeZone
        {
            get => _mainTimeZone;
            set => Set(ref _mainTimeZone, value ?? "");
        }

        // The third time zone, the same way.
        private string _thirdTimeZone = "";
        public string ThirdTimeZone
        {
            get => _thirdTimeZone;
            set => Set(ref _thirdTimeZone, value ?? "");
        }

        // Whether the clock names each of its time zones: the computer's own (the main one), the second and the third.
        private bool _showMainZoneName;
        public bool ShowMainZoneName
        {
            get => _showMainZoneName;
            set => Set(ref _showMainZoneName, value);
        }

        private bool _showSecondZoneName = true;
        public bool ShowSecondZoneName
        {
            get => _showSecondZoneName;
            set => Set(ref _showSecondZoneName, value);
        }

        private bool _showThirdZoneName = true;
        public bool ShowThirdZoneName
        {
            get => _showThirdZoneName;
            set => Set(ref _showThirdZoneName, value);
        }

        // The name the clock shows for each zone instead of its place's ("Heart" for "Jerusalem"); empty is the place's.
        private string _mainZoneLabel = "";
        public string MainZoneLabel
        {
            get => _mainZoneLabel;
            set => Set(ref _mainZoneLabel, value ?? "");
        }

        private string _secondZoneLabel = "";
        public string SecondZoneLabel
        {
            get => _secondZoneLabel;
            set => Set(ref _secondZoneLabel, value ?? "");
        }

        private string _thirdZoneLabel = "";
        public string ThirdZoneLabel
        {
            get => _thirdZoneLabel;
            set => Set(ref _thirdZoneLabel, value ?? "");
        }

        // Which of the three time zones the calendar goes by for the date, the change of the day and the times of the
        // day: 0 the main one (the computer's), 1 the second, 2 the third.
        private int _calendarZone;
        public int CalendarZone
        {
            get => _calendarZone;
            set => Set(ref _calendarZone, value < 0 || value > 2 ? 0 : value);
        }

        // The light Modern Glass became the main theme: set once the theme of earlier runs was moved to it.
        private bool _lightThemeIntroduced;
        public bool LightThemeIntroduced
        {
            get => _lightThemeIntroduced;
            set => Set(ref _lightThemeIntroduced, value);
        }

        // The calendar's grid shows a Hebrew month (1 Kislev to its end) instead of a civil one.
        private bool _calendarHebrewGrid;
        public bool CalendarHebrewGrid
        {
            get => _calendarHebrewGrid;
            set => Set(ref _calendarHebrewGrid, value);
        }

        // Keys of the yahrzeit figures (Yahrzeits.All) the user switched off in the calendar; everyone is on until then.
        private List<string> _hiddenYahrzeits = [];
        public List<string> HiddenYahrzeits
        {
            get => _hiddenYahrzeits;
            set => Set(ref _hiddenYahrzeits, value ?? []);
        }

        private bool _moveActivatedWindowsToCurrentDesktop;
        public bool MoveActivatedWindowsToCurrentDesktop
        {
            get => _moveActivatedWindowsToCurrentDesktop;
            set => Set(ref _moveActivatedWindowsToCurrentDesktop, value);
        }

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

        private bool _showClockSeconds = true;
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
}
