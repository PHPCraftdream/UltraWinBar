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
        public static readonly DependencyProperty OrdinaryDateTextProperty = DependencyProperty.Register(nameof(OrdinaryDateText), typeof(string), typeof(Clock));
        public string OrdinaryDateText { get => (string)GetValue(OrdinaryDateTextProperty); set => SetValue(OrdinaryDateTextProperty, value); }
        public static readonly DependencyProperty WeekdayTextProperty = DependencyProperty.Register(nameof(WeekdayText), typeof(string), typeof(Clock));
        public string WeekdayText { get => (string)GetValue(WeekdayTextProperty); set => SetValue(WeekdayTextProperty, value); }
        private CultureInfo clockCulture = CultureInfo.CurrentCulture;

        private readonly DispatcherTimer _clock = new DispatcherTimer(DispatcherPriority.Background);
        private bool _isLoaded;
        private DateTime _zonesRead = DateTime.UtcNow;

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
                case nameof(Settings.SecondTimeZone):
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
            SetConverterCultureRecursively(ClockTip, userCulture);
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
            Now = DateTime.Now;
            UpdateClockText();
            UpdateBottomSpacing();
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

        private void UpdateClockText()
        {
            var value = Now == default ? DateTime.Now : Now;
            string timeText = value.ToString("t", clockCulture);
            if (TimeText != timeText) TimeText = timeText;
            UpdateSecondTime(value);
            string dateText = value.ToString("d", clockCulture);
            if (OrdinaryDateText != dateText) OrdinaryDateText = dateText;
            if (Settings.Instance.ShowHebrewDate)
            {
                // With a place chosen the Hebrew day turns at its sunset: until midnight the day is the next one,
                // with the day that is still on in brackets.
                HebrewDayNow hebrewDay = HebrewDayClock.Now(value.ToUniversalTime(), Settings.Instance.HebrewPlace);
                string weekday = HebrewClockFormatter.DayName(hebrewDay.Day.DayOfWeek, hebrewDay.Civil?.DayOfWeek);
                if (WeekdayText != weekday) WeekdayText = weekday;
                string hebrewDateText = HebrewClockFormatter.FormatDate(hebrewDay.Day, Settings.Instance.Language, null, hebrewDay.Civil);
                if (HebrewDateText != hebrewDateText) HebrewDateText = hebrewDateText;
            }
            else
            {
                string weekdayText = HebrewClockFormatter.DayName(value.DayOfWeek);
                if (WeekdayText != weekdayText) WeekdayText = weekdayText;
            }
        }

        // The time in the second time zone: "15:05 Jerusalem", with "+1" or "-1" when it is already the next or still the
        // previous day there. Nothing when no zone is chosen or this computer does not know the one chosen.
        private void UpdateSecondTime(DateTime computerTime)
        {
            string text = "";
            string zone = Settings.Instance.SecondTimeZone;
            DateTime? there = string.IsNullOrEmpty(zone) ? null : ZoneClock.Now(zone, computerTime.ToUniversalTime());
            if (there.HasValue)
            {
                int days = (there.Value.Date - computerTime.Date).Days;
                text = there.Value.ToString("t", clockCulture) + " " + ZoneClock.ShortName(zone) + (days == 0 ? "" : days > 0 ? " +" + days : " " + days);
            }
            if (SecondTimeText != text) SecondTimeText = text;
            Visibility visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (SecondTimeVisibility != visibility) SecondTimeVisibility = visibility;
        }

        private void UpdateClockTemplate() => SetResourceReference(TemplateProperty,
            Settings.Instance.ShowHebrewDate ? "HebrewClockTemplate"
            : string.IsNullOrEmpty(Settings.Instance.SecondTimeZone) ? "ClockTemplateKey" : "TwoZoneClockTemplate");

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

        private void Clock_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressPosition = e.GetPosition(this);

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

            // A click on the Hebrew date opens the Jewish calendar, whatever the clock does on a click elsewhere.
            if (Settings.Instance.ShowHebrewDate && Template?.FindName("HebrewDateHost", this) is FrameworkElement hebrewDateHost && hebrewDateHost.IsMouseOver)
            {
                e.Handled = true;
                CalendarWindow.ShowSingle();
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
