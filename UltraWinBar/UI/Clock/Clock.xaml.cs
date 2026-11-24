using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.UWPInterop;
using Microsoft.Win32;
using UltraWinBar.Utilities;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace UltraWinBar.Controls
{
    /// <summary>
    /// Interaction logic for Clock.xaml
    /// </summary>
    public partial class Clock : UserControl
    {
        public static readonly DependencyProperty NowProperty = DependencyProperty.Register(nameof(Now), typeof(DateTime), typeof(Clock));

        public DateTime Now
        {
            get => (DateTime)GetValue(NowProperty);
            set => SetValue(NowProperty, value);
        }

        public static readonly DependencyProperty HebrewDateTextProperty = DependencyProperty.Register(
            nameof(HebrewDateText), typeof(string), typeof(Clock), new PropertyMetadata(string.Empty));
        public string HebrewDateText
        {
            get => (string)GetValue(HebrewDateTextProperty);
            set => SetValue(HebrewDateTextProperty, value);
        }
        public static readonly DependencyProperty TimeTextProperty = DependencyProperty.Register(nameof(TimeText), typeof(string), typeof(Clock));
        public string TimeText { get => (string)GetValue(TimeTextProperty); set => SetValue(TimeTextProperty, value); }
        public static readonly DependencyProperty SecondTimeTextProperty = DependencyProperty.Register(nameof(SecondTimeText), typeof(string), typeof(Clock), new PropertyMetadata(string.Empty));
        public string SecondTimeText { get => (string)GetValue(SecondTimeTextProperty); set => SetValue(SecondTimeTextProperty, value); }
        public static readonly DependencyProperty SecondTimeVisibilityProperty = DependencyProperty.Register(nameof(SecondTimeVisibility), typeof(Visibility), typeof(Clock), new PropertyMetadata(Visibility.Collapsed));
        public Visibility SecondTimeVisibility { get => (Visibility)GetValue(SecondTimeVisibilityProperty); set => SetValue(SecondTimeVisibilityProperty, value); }
        public static readonly DependencyProperty ThirdTimeTextProperty = DependencyProperty.Register(nameof(ThirdTimeText), typeof(string), typeof(Clock), new PropertyMetadata(string.Empty));
        public string ThirdTimeText { get => (string)GetValue(ThirdTimeTextProperty); set => SetValue(ThirdTimeTextProperty, value); }
        public static readonly DependencyProperty ThirdTimeVisibilityProperty = DependencyProperty.Register(nameof(ThirdTimeVisibility), typeof(Visibility), typeof(Clock), new PropertyMetadata(Visibility.Collapsed));
        public Visibility ThirdTimeVisibility { get => (Visibility)GetValue(ThirdTimeVisibilityProperty); set => SetValue(ThirdTimeVisibilityProperty, value); }
        public static readonly DependencyProperty OrdinaryDateTextProperty = DependencyProperty.Register(nameof(OrdinaryDateText), typeof(string), typeof(Clock));
        public string OrdinaryDateText { get => (string)GetValue(OrdinaryDateTextProperty); set => SetValue(OrdinaryDateTextProperty, value); }

        // A candle by the weekday when the Hebrew day has a yahrzeit that is shown; the tree of the sefirot and the day during the Omer.
        public static readonly DependencyProperty YahrzeitVisibilityProperty = DependencyProperty.Register(nameof(YahrzeitVisibility), typeof(Visibility), typeof(Clock), new PropertyMetadata(Visibility.Collapsed));
        public Visibility YahrzeitVisibility { get => (Visibility)GetValue(YahrzeitVisibilityProperty); set => SetValue(YahrzeitVisibilityProperty, value); }

        public static readonly DependencyProperty OmerTextProperty = DependencyProperty.Register(nameof(OmerText), typeof(string), typeof(Clock), new PropertyMetadata(""));
        public string OmerText { get => (string)GetValue(OmerTextProperty); set => SetValue(OmerTextProperty, value); }

        public static readonly DependencyProperty OmerVisibilityProperty = DependencyProperty.Register(nameof(OmerVisibility), typeof(Visibility), typeof(Clock), new PropertyMetadata(Visibility.Collapsed));
        public Visibility OmerVisibility { get => (Visibility)GetValue(OmerVisibilityProperty); set => SetValue(OmerVisibilityProperty, value); }
        public static readonly DependencyProperty WeekdayTextProperty = DependencyProperty.Register(nameof(WeekdayText), typeof(string), typeof(Clock));
        public string WeekdayText { get => (string)GetValue(WeekdayTextProperty); set => SetValue(WeekdayTextProperty, value); }
        private CultureInfo clockCulture = CultureInfo.CurrentCulture;

        private readonly DispatcherTimer _clock = new DispatcherTimer(DispatcherPriority.Background);
        private bool _isLoaded;
        private DateTime _zonesRead = DateTime.UtcNow;
        private DateTime _utcNow;

        private const int LOCALE_NAME_MAX_LENGTH = 85;
        private const int TickMarginMilliseconds = 20;

        public Clock()
        {
            InitializeComponent();
            DataContext = this;

            _clock.Tick += Clock_Tick;
        }

        private void Initialize()
        {
            UpdateClockTemplate();
            UpdateUserCulture();
            if (Settings.Instance.ShowClock)
            {
                StartClock();
            }
            else
            {
                Visibility = Visibility.Collapsed;
            }

            WeakSubscriptions.SubscribeSettings(Settings_PropertyChanged);
            SystemEvents.TimeChanged += TimeChanged;
            SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
            SystemEvents.PowerModeChanged += PowerModeChanged;
            IsVisibleChanged += OnIsVisibleChanged;

            // The Jewish calendar is made ahead, so a click on the Hebrew date only shows it: when the pointer comes to
            // the clock, and once a while after the start.
            MouseEnter += (_, _) => PrepareCalendar();
            if (!_calendarWarmupQueued)
            {
                _calendarWarmupQueued = true;
                var warmup = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(10) };
                warmup.Tick += (_, _) => { warmup.Stop(); PrepareCalendar(); };
                warmup.Start();
            }
        }

        private static bool _calendarWarmupQueued;

        private void PrepareCalendar()
        {
            if (Settings.Instance.ShowHebrewDate)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(CalendarWindow.Prepare));
        }

        private void StartClock()
        {
            Visibility = Visibility.Visible;

            SyncClockRunning();
        }

        private void StopClock()
        {
            Visibility = Visibility.Collapsed;

            SyncClockRunning();
        }

        // Panels that don't host the clock keep it loaded but collapsed; tick only while visible.
        private void SyncClockRunning()
        {
            if (IsVisible && Settings.Instance.ShowClock)
            {
                SetTime();
                RescheduleClock();
            }
            else
            {
                _clock.Stop();
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncClockRunning();

        // Re-arms for the next second/minute boundary instead of polling every 200ms forever.
        private void RescheduleClock()
        {
            bool secondsVisible = ClockCanShowSeconds(Settings.Instance.ShowClockSeconds,
                Settings.Instance.OverrideClockFormat, Settings.Instance.ClockFormat);
            _clock.Stop();
            _clock.Interval = GetNextTickDelay(DateTime.Now, secondsVisible);
            _clock.Start();
        }

        internal static bool ClockCanShowSeconds(bool showClockSeconds, bool overrideClockFormat, string clockFormat) =>
            showClockSeconds || (overrideClockFormat && clockFormat != null && clockFormat.Contains('s'));

        internal static TimeSpan GetNextTickDelay(DateTime now, bool secondsVisible)
        {
            DateTime boundary = secondsVisible
                ? new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind).AddSeconds(1)
                : new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind).AddMinutes(1);
            return boundary - now + TimeSpan.FromMilliseconds(TickMarginMilliseconds);
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.ShowHebrewDate):
                    UpdateClockTemplate();
                    UpdateUserCulture();
                    SetTime();
                    break;
                case nameof(Settings.ShowClock):
                    if (Settings.Instance.ShowClock)
                    {
                        StartClock();
                    }
                    else
                    {
                        StopClock();
                    }
                    break;
                case nameof(Settings.ShowClockSeconds):
                case nameof(Settings.OverrideClockFormat):
                case nameof(Settings.ClockFormat):
                    UpdateUserCulture();
                    if (_clock.IsEnabled)
                    {
                        SetTime();
                        RescheduleClock();
                    }
                    break;
                case nameof(Settings.HebrewPlace):
                    UpdateClockText();
                    break;
                case nameof(Settings.MainTimeZone):
                case nameof(Settings.SecondTimeZone):
                case nameof(Settings.ThirdTimeZone):
                case nameof(Settings.ShowMainZoneName):
                case nameof(Settings.ShowSecondZoneName):
                case nameof(Settings.ShowThirdZoneName):
                case nameof(Settings.MainZoneLabel):
                case nameof(Settings.SecondZoneLabel):
                case nameof(Settings.ThirdZoneLabel):
                case nameof(Settings.CalendarZone):
                case nameof(Settings.SecondPlace):
                case nameof(Settings.ThirdPlace):
                    UpdateClockTemplate();
                    UpdateUserCulture();
                    SetTime();
                    break;
                case nameof(Settings.Language):
                case nameof(Settings.OverrideAMPMDesignators):
                case nameof(Settings.AMDesignator):
                case nameof(Settings.PMDesignator):
                    UpdateUserCulture();
                    break;
            }
        }

        private void Clock_Tick(object sender, EventArgs args)
        {
            SetTime();
            RescheduleClock();
        }

        private void TimeChanged(object sender, EventArgs e)
        {
            ZoneClock.Refresh();
            if (_clock.IsEnabled)
            {
                SetTime();
                RescheduleClock();
            }
        }

        private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume && _clock.IsEnabled)
            {
                SetTime();
                RescheduleClock();
            }
        }

        private static void SetConverterCultureRecursively(DependencyObject main, CultureInfo ci)
        {
            if (main != null)
            {
                var binding = BindingOperations.GetBinding(main, TextBlock.TextProperty);

                if (binding != null)
                {
                    BindingOperations.SetBinding(main, TextBlock.TextProperty,
                        new Binding(binding.Path.Path)
                            { StringFormat = binding.StringFormat, ConverterCulture = ci });
                }

                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(main); i++)
                {
                    if (VisualTreeHelper.GetChild(main, i) is UIElement sub)
                    {
                        SetConverterCultureRecursively(sub, ci);
                    }
                }
            }
        }

        private void UpdateUserCulture()
        {
            CultureInfo userCulture;

            try
            {
                string localeName = GetUserDefaultLocaleNameWrapper();

                if (!string.IsNullOrEmpty(localeName))
                {
                    userCulture = new CultureInfo(localeName);
                }
                else
                {
                    // Fallback: use LCID if locale name isn't available.
                    int lcid = GetUserDefaultLCID();
                    userCulture = new CultureInfo(lcid);
                }
            }
            catch (Exception e)
            {
                ShellLogger.Error($"Clock: Unable to get the user culture: {e.Message}, defaulting to current culture");
                userCulture = CultureInfo.CurrentCulture;
            }

            if (userCulture.IsReadOnly)
            {
                userCulture = (CultureInfo)userCulture.Clone();
            }

            if (Settings.Instance.ShowClockSeconds)
            {
                userCulture.DateTimeFormat.ShortTimePattern = userCulture.DateTimeFormat.LongTimePattern;
            }

            // Override culture info if desired, inserting newlines where appropriate
            if (Settings.Instance.OverrideClockFormat && !string.IsNullOrEmpty(Settings.Instance.ClockFormat))
            {
                userCulture.DateTimeFormat.ShortTimePattern = Settings.Instance.ClockFormat.Replace("\\n", "\n");
            }

            if (Settings.Instance.OverrideAMPMDesignators)
            {
                if (Settings.Instance.AMDesignator != "")
                {
                    userCulture.DateTimeFormat.AMDesignator = Settings.Instance.AMDesignator;
                }
                if (Settings.Instance.PMDesignator != "")
                {
                    userCulture.DateTimeFormat.PMDesignator = Settings.Instance.PMDesignator;
                }
            }

            clockCulture = userCulture;
            UpdateClockText();
            SetConverterCultureRecursively(this, userCulture);
        }

        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Locale)
            {
                CultureInfo.CurrentCulture.ClearCachedData();
                UpdateUserCulture();
            }
        }

        private void SetTime()
        {
            // Time zone rules change by law and Windows updates them: read them again now and then.
            if (DateTime.UtcNow - _zonesRead > TimeSpan.FromHours(6))
            {
                ZoneClock.Refresh();
                _zonesRead = DateTime.UtcNow;
            }
            // The clock shows the main time zone: the computer's own unless another is chosen in the settings.
            _utcNow = DateTime.UtcNow;
            Now = ClockZones.MainTime(_utcNow);
            UpdateClockText();
            UpdateBottomSpacing();
            UpdateClockTemplate(); // the panel may have moved to another edge
        }

        private void UpdateBottomSpacing()
        {
            double bottom = 0;
            if (Window.GetWindow(this) is Taskbar taskbar && taskbar.Orientation == Orientation.Vertical)
            {
                double scale = Settings.Instance.TaskbarScale * VisualTreeHelper.GetDpi(this).DpiScaleY;
                if (scale > 0) bottom = 22.5 / scale;
            }

            if (Math.Abs(Margin.Bottom - bottom) > 0.05)
                Margin = new Thickness(0, 0, 0, bottom);
        }

        // On a vertical panel the zone's name goes under the time: one long line would shrink the whole clock.
        private string NameSeparator => ClockZones.NameSeparator(Window.GetWindow(this) is Taskbar { Orientation: Orientation.Vertical });

        private void UpdateClockText()
        {
            DateTime utc = _utcNow == default ? DateTime.UtcNow : _utcNow;
            var value = Now == default ? ClockZones.MainTime(utc) : Now;
            string timeText = value.ToString("t", clockCulture) + (ClockZones.ShowsName(ClockZones.Main) ? NameSeparator + ClockZones.NameOf(ClockZones.Main) : "");
            if (TimeText != timeText) TimeText = timeText;
            UpdateZoneTimes(value, utc);
            string dateText = value.ToString("d", clockCulture);
            if (OrdinaryDateText != dateText) OrdinaryDateText = dateText;
            if (Settings.Instance.ShowHebrewDate)
            {
                // With a place chosen the Hebrew day turns at its sunset: until midnight the day is the next one,
                // with the day that is still on in brackets.
                HebrewDayNow hebrewDay = HebrewDayClock.Now(utc, ClockZones.CalendarPlace());
                string weekday = HebrewClockFormatter.DayName(hebrewDay.Day.DayOfWeek, hebrewDay.Civil?.DayOfWeek);
                if (WeekdayText != weekday) WeekdayText = weekday;
                string hebrewDateText = HebrewClockFormatter.FormatDate(hebrewDay.Day, Settings.Instance.Language, null, hebrewDay.Civil);
                if (HebrewDateText != hebrewDateText) HebrewDateText = hebrewDateText;
                var marks = DayDetailsBuilder.MarksOf(hebrewDay.Day, Settings.Instance.HiddenYahrzeits);
                Visibility yahrzeit = marks.Yahrzeit ? Visibility.Visible : Visibility.Collapsed;
                if (YahrzeitVisibility != yahrzeit) YahrzeitVisibility = yahrzeit;
                int omer = marks.Omer;
                string omerText = omer > 0 ? omer.ToString(CultureInfo.InvariantCulture) : "";
                if (OmerText != omerText) OmerText = omerText;
                Visibility omerVisibility = omer > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (OmerVisibility != omerVisibility) OmerVisibility = omerVisibility;
            }
            else
            {
                string weekdayText = HebrewClockFormatter.DayName(value.DayOfWeek);
                if (WeekdayText != weekdayText) WeekdayText = weekdayText;
            }
        }

        // The time in the second and the third time zone: "15:05 Jerusalem", with "+1" or "-1" when it is already the next or
        // still the previous day there; the name when the zone's checkbox is set. Nothing for a zone that is not chosen
        // or that this computer does not know.
        private void UpdateZoneTimes(DateTime mainTime, DateTime utc)
        {
            string second = ZoneText(ClockZones.Second, mainTime, utc);
            if (SecondTimeText != second) SecondTimeText = second;
            Visibility secondVisibility = second.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (SecondTimeVisibility != secondVisibility) SecondTimeVisibility = secondVisibility;
            string third = ZoneText(ClockZones.Third, mainTime, utc);
            if (ThirdTimeText != third) ThirdTimeText = third;
            Visibility thirdVisibility = third.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ThirdTimeVisibility != thirdVisibility) ThirdTimeVisibility = thirdVisibility;
        }

        // The day there is counted from the day of the main time zone.
        private string ZoneText(int zone, DateTime mainTime, DateTime utc)
        {
            string id = ClockZones.IdOf(zone);
            DateTime? there = string.IsNullOrEmpty(id) ? null : ZoneClock.Now(id, utc);
            if (!there.HasValue) return "";
            int days = (there.Value.Date - mainTime.Date).Days;
            return there.Value.ToString("t", clockCulture) + (ClockZones.ShowsName(zone) ? NameSeparator + ClockZones.NameOf(zone) : "") + (days == 0 ? "" : days > 0 ? " +" + days : " " + days);
        }

        private string _templateKey;

        // A vertical panel has a template of its own: the zones and the dates each in a frame.
        private void UpdateClockTemplate()
        {
            bool vertical = Window.GetWindow(this) is Taskbar { Orientation: Orientation.Vertical };
            string key = Settings.Instance.ShowHebrewDate ? (vertical ? "HebrewClockVerticalTemplate" : "HebrewClockTemplate")
                : !ClockZones.IsSet(ClockZones.Second) && !ClockZones.IsSet(ClockZones.Third) && !ClockZones.ShowsName(ClockZones.Main) ? "ClockTemplateKey" : "TwoZoneClockTemplate";
            if (key == _templateKey) return;
            _templateKey = key;
            SetResourceReference(TemplateProperty, key);
        }

        private static string GetUserDefaultLocaleNameWrapper()
        {
            var sb = new StringBuilder(LOCALE_NAME_MAX_LENGTH);
            int ret = GetUserDefaultLocaleName(sb, sb.Capacity);
            return ret > 0 ? sb.ToString() : null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetUserDefaultLocaleName(StringBuilder lpLocaleName, int cchLocaleName);

        [DllImport("kernel32.dll")]
        private static extern int GetUserDefaultLCID();

        private Point? _pressPosition;
        private bool _shellFlyoutWasForegroundAtPress;

        private int _pressClickCount;

        private bool IsOverHebrewDate() =>
            Settings.Instance.ShowHebrewDate && Template?.FindName("HebrewDateHost", this) is FrameworkElement hebrewDateHost && hebrewDateHost.IsMouseOver;

        private void Clock_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressPosition = e.GetPosition(this);
            _pressClickCount = e.ClickCount;

            // Capture this before the click itself can steal focus away from an already-open
            // flyout (real shell flyouts like Action Center dismiss themselves on focus loss).
            IntPtr hwndForeground = ManagedShell.Interop.NativeMethods.GetForegroundWindow();
            StringBuilder foregroundClass = new StringBuilder(256);
            ManagedShell.Interop.NativeMethods.GetClassName(hwndForeground, foregroundClass, foregroundClass.Capacity);
            _shellFlyoutWasForegroundAtPress = foregroundClass.ToString() == "Windows.UI.Core.CoreWindow";
        }

        private void Clock_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_pressPosition == null)
            {
                return;
            }

            Point pressPosition = _pressPosition.Value;
            _pressPosition = null;

            Point releasePosition = e.GetPosition(this);
            if (Math.Abs(releasePosition.X - pressPosition.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(releasePosition.Y - pressPosition.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                // Moved past the click threshold since mouse-down — this was a drag
                // (e.g. to another taskbar), not a click, so don't act on it.
                return;
            }

            // A click on the Hebrew date opens the Jewish calendar, whatever the clock does on a click elsewhere. The second
            // click of a double click shows it too (it does not close what the first one opened).
            if (IsOverHebrewDate())
            {
                e.Handled = true;
                CalendarWindow.ShowSingle(this, toggle: _pressClickCount < 2);
                return;
            }

            if (Settings.Instance.ClockClickAction == ClockClickOption.DoNothing)
            {
                return;
            }

            e.Handled = true;

            switch (Settings.Instance.ClockClickAction)
            {
                case ClockClickOption.OpenModernCalendar:
                    Point screenPosition = PointToScreen(new(0, 0));
                    ManagedShell.Interop.NativeMethods.Rect rect = new(
                        (int)screenPosition.X, (int)screenPosition.Y,
                        (int)(screenPosition.X + RenderSize.Width),
                        (int)(screenPosition.Y + RenderSize.Height)
                    );
                    ImmersiveShellHelper.ShowClockFlyout(rect);
                    break;
                case ClockClickOption.OpenAeroCalendar:
                    IntPtr hWnd = (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource).Handle;
                    ClockFlyoutLauncher.ShowAeroClockFlyout(hWnd);
                    break;
                case ClockClickOption.OpenNotificationCenter:
                    if (_shellFlyoutWasForegroundAtPress)
                    {
                        // Already open when this click started — clicking the clock just took
                        // focus away from it, which dismisses it on its own. Calling Show again
                        // here would immediately reopen it instead of leaving it closed.
                        break;
                    }

                    if (EnvironmentHelper.IsWindows10RS4OrBetter)
                    {
                        ImmersiveShellHelper.ShowActionCenter();
                    }
                    else
                    {
                        ShellHelper.ShowActionCenter();
                    }
                    break;
            }
        }

        private void Clock_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // On the Hebrew date a double click is a click: the calendar, not the system's date and time settings.
            if (IsOverHebrewDate())
            {
                e.Handled = true;
                return;
            }

            ShellHelper.StartProcess("timedate.cpl");

            e.Handled = true;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                Initialize();

                _isLoaded = true;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopClock();

            WeakSubscriptions.UnsubscribeSettings(Settings_PropertyChanged);
            SystemEvents.TimeChanged -= TimeChanged;
            SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
            SystemEvents.PowerModeChanged -= PowerModeChanged;
            IsVisibleChanged -= OnIsVisibleChanged;

            _isLoaded = false;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateUserCulture();
        }
    }
}
