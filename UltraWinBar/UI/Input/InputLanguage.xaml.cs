using System;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.Common.Logging;
using UltraWinBar.Converters;
using UltraWinBar.Utilities;
using WinForms = System.Windows.Forms;

namespace UltraWinBar.Controls
{
    public partial class InputLanguage : UserControl
    {
        public static DependencyProperty LocaleIdentifierProperty = DependencyProperty.Register(nameof(LocaleIdentifier), typeof(CultureInfo), typeof(InputLanguage));

        public CultureInfo LocaleIdentifier
        {
            get { return (CultureInfo)GetValue(LocaleIdentifierProperty); }
            set { SetValue(LocaleIdentifierProperty, value); }
        }

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(InputLanguage), new PropertyMetadata(OnHostChanged));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        private readonly DispatcherTimer layoutWatch = new DispatcherTimer(DispatcherPriority.Background);
        private readonly CultureInfoToLocaleNameConverter _localeConverter = new CultureInfoToLocaleNameConverter();

        private bool _isLoaded;
        private IntPtr _lastHkl = IntPtr.Zero;

        public InputLanguage()
        {
            InitializeComponent();
            DataContext = this;

            layoutWatch.Interval = TimeSpan.FromMilliseconds(200);
            layoutWatch.Tick += LayoutWatchTick;
        }

        // Host resolves via a FindAncestor binding, which can still be null the first time
        // this runs (Loaded can fire before the binding settles). Requiring a non-null Host
        // — and re-checking via OnHostChanged once it arrives — avoids every taskbar
        // defaulting to "show" during that window, which would run one watch timer per
        // taskbar simultaneously.
        private bool ShouldShow => Settings.Instance.ShowInputLanguage &&
                                    Host != null && Host.AppBarEdge == Settings.Instance.ResolvedLanguageEdge;

        private static void OnHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InputLanguage inputLanguage && inputLanguage._isLoaded)
            {
                inputLanguage.UpdateVisibility();
            }
        }

        private void Initialize()
        {
            UpdateVisibility();

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void UpdateVisibility()
        {
            if (ShouldShow)
            {
                StartWatch();
            }
            else
            {
                StopWatch();
            }
        }

        // Raw HKL of the focused thread, as KeyboardLayoutHelper finds it, without its per-call culture work.
        private static IntPtr GetActiveKeyboardLayout()
        {
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            IntPtr hwnd = GetGUIThreadInfo(0, ref info) && info.hwndFocus != IntPtr.Zero
                ? info.hwndFocus
                : GetForegroundWindow();

            uint threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
            return GetKeyboardLayout(threadId);
        }

        // A zero language id (e.g. secure desktop) is skipped instead of throwing.
        private bool TrySetLocaleIdentifier(IntPtr hkl)
        {
            int langId = unchecked((short)hkl.ToInt64());
            if (langId == 0)
            {
                return false;
            }

            try
            {
                LocaleIdentifier = CultureInfo.GetCultureInfo(langId);
                return true;
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"Error getting locale identifier: {ex.Message}");
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public int rcCaretLeft, rcCaretTop, rcCaretRight, rcCaretBottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        private void StartWatch()
        {
            TrySetLocaleIdentifier(GetActiveKeyboardLayout());

            layoutWatch.Start();

            Visibility = Visibility.Visible;
        }

        private void JapaneseImeAdd()
        {
            var ChkControl = InputLanguageDockPanel.Children
                                .OfType<JapaneseIme>()
                                .FirstOrDefault();

            if (ChkControl != null)
            {
                return;
            }

            var NewControl = new JapaneseIme();
            InputLanguageDockPanel.Children.Add(NewControl);
        }
        
        private void JapaneseImeRemove()
        {
            var DelControl = InputLanguageDockPanel.Children
                                .OfType<JapaneseIme>()
                                .FirstOrDefault();

            if (DelControl != null)
            {
                InputLanguageDockPanel.Children.Remove(DelControl);
            }
        }

        private int InstalledLanguagesCount()
        {
            int LangCount = 0;

            foreach (System.Windows.Forms.InputLanguage CurLang in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
            {
                LangCount++;
            }

            return LangCount;
        }

        private void LayoutWatchTick(object sender, EventArgs args)
        {
            IntPtr hkl = GetActiveKeyboardLayout();
            if (hkl == _lastHkl)
            {
                return;
            }
            _lastHkl = hkl;

            if (!TrySetLocaleIdentifier(hkl))
            {
                return;
            }

            string localeName = (string)_localeConverter.Convert(LocaleIdentifier, typeof(string), "TwoLetterIsoLanguageName", CultureInfo.InvariantCulture);

            if (localeName == "JA")
            {
                JapaneseImeAdd();

                if (InstalledLanguagesCount() == 1)
                {
                    InputLanguageText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    InputLanguageText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                JapaneseImeRemove();
                InputLanguageText.Visibility = Visibility.Visible;
            }
        }

        private void StopWatch()
        {
            layoutWatch.Stop();

            Visibility = Visibility.Collapsed;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.ShowInputLanguage) ||
                e.PropertyName == nameof(Settings.LanguageEdge) ||
                e.PropertyName == nameof(Settings.Edge) ||
                e.PropertyName == nameof(Settings.AdditionalEdges))
            {
                UpdateVisibility();
            }
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
            StopWatch();
            
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

            _isLoaded = false;
        }

        private void ShowLanguageMenu()
        {
            var menu = new ContextMenu();

            foreach (WinForms.InputLanguage lang in WinForms.InputLanguage.InstalledInputLanguages)
            {
                string code = lang.Culture.TwoLetterISOLanguageName.ToUpperInvariant();
                string displayName = lang.Culture.DisplayName;
                bool multiLayout = false;

                foreach (WinForms.InputLanguage nestedLang in WinForms.InputLanguage.InstalledInputLanguages)
                {
                    if (nestedLang.Culture.DisplayName == lang.Culture.DisplayName && nestedLang.LayoutName != lang.LayoutName)
                    {
                        multiLayout = true;
                        break;
                    }
                }

                if (multiLayout)
                {
                    displayName = string.Format((string)FindResource("input_switcher_item_format"), lang.Culture.DisplayName, lang.LayoutName);
                }

                var item = new MenuItem
                {
                    Header = new InputLanguageMenuItem(code, displayName),
                    Tag = lang,
                    IsCheckable = true,
                    IsChecked = lang.Equals(WinForms.InputLanguage.CurrentInputLanguage)
                };

                item.Click += (s, e) =>
                {
                    WinForms.InputLanguage.CurrentInputLanguage = (WinForms.InputLanguage)((MenuItem)s).Tag;
                };

                menu.Items.Add(item);
            }

            menu.Closed += (sender, e) =>
            {
                Host?.RemoveOpenMenu();
            };

            menu.PlacementTarget = InputLanguageButton;
            Host?.AddOpenMenu();
            menu.IsOpen = true;
        }

        private void InputLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLanguageMenu();
        }
    };
}