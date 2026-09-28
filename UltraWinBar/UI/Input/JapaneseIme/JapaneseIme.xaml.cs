using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ManagedShell.AppBar;
using static ManagedShell.Interop.NativeMethods;
using Microsoft.Win32;
using UltraWinBar.Utilities;

namespace UltraWinBar.Controls
{
    /// <summary>
    /// Interaction logic for JapaneseIme.xaml
    /// </summary>
    public partial class JapaneseIme : UserControl
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        // IME
        [DllImport("imm32.dll")]
        static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        const int WM_IME_CONTROL = 0x0283;

        // SendMessageTimeout flags/timeout for ImeChk, so a hung foreground app
        // cannot block the taskbar UI thread.
        const uint SMTO_ABORTIFHUNG = 0x0002;
        const uint IME_CHK_TIMEOUT_MS = 100;

        const uint IME_CMODE_NATIVE = 0x0001;
        const uint IME_CMODE_KATAKANA = 0x0002;  // only effect under IME_CMODE_NATIVE
        const uint IME_CMODE_FULLSHAPE = 0x0008;
        const uint IMC_GETCONVERSIONMODE = 0x0001;
        const uint IMC_SETCONVERSIONMODE = 0x0002;
        const uint IMC_GETSENTENCEMODE = 0x0003;
        const uint IMC_SETSENTENCEMODE = 0x0004;
        const uint IMC_GETOPENSTATUS = 0x0005;
        const uint IMC_SETOPENSTATUS = 0x0006;

        // Regstory
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegOpenKey(UIntPtr hKey, string lpSubKey, out UIntPtr phkResult);

        [DllImport("advapi32.dll")]
        static extern int RegCloseKey(UIntPtr hKey);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegQueryValueEx(UIntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, out uint lpData, ref uint lpcbData);

        const int ERROR_SUCCESS = 0;

        const uint REG_DWORD = 4; // 32-bit number
        const uint REG_SIZE_DWORD = 4; // 32-bit number

        // SendInput
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        const ushort VK_SHIFT = 0x10;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_F10 = 0x79;
        const ushort VK_OEM_COPY = 0xF2;

        private IntPtr hWndCurFg;
        private bool NewImeEnabled;
        private bool CurInputRoma;
        private uint CurInputMode;
        private uint CurImmConversion;

        private readonly DispatcherTimer ImeCheckTimer = new DispatcherTimer(DispatcherPriority.Background);

        private bool _isLoaded;

        public static readonly DependencyProperty JapaneseImeEnabledProperty = DependencyProperty.Register(nameof(JapaneseImeEnabled), typeof(bool), typeof(JapaneseIme));

        public bool JapaneseImeEnabled
        {
            get => (bool)GetValue(JapaneseImeEnabledProperty);
            set => SetValue(JapaneseImeEnabledProperty, value);
        }

        public static readonly DependencyProperty JapaneseImeTipProperty = DependencyProperty.Register(nameof(JapaneseImeTip), typeof(string), typeof(JapaneseIme));

        public string JapaneseImeTip
        {
            get => (string)GetValue(JapaneseImeTipProperty);
            set => SetValue(JapaneseImeTipProperty, value);
        }

        public static readonly DependencyProperty JapaneseImeStatusProperty = DependencyProperty.Register(nameof(JapaneseImeStatus), typeof(string), typeof(JapaneseIme));

        public string JapaneseImeStatus
        {
            get => (string)GetValue(JapaneseImeStatusProperty);
            set => SetValue(JapaneseImeStatusProperty, value);
        }

        public JapaneseIme()
        {
            InitializeComponent();
            DataContext = this;

            ImeCheckTimer.Interval = TimeSpan.FromMilliseconds(200);
            ImeCheckTimer.Tick += ImeChk_Event;
        }

        private void Initialize()
        {
            hWndCurFg = (IntPtr)0;
            NewImeEnabled = false;
            CurInputRoma = false;
            CurInputMode = 0;
            CurImmConversion = 0;

            JapaneseImeTip = (string)this.FindResource("ime_input_tip_disabled");
            JapaneseImeStatus = @"";

            ImeChk();
            ImeCheckTimer.Start();
        }

        // Non-blocking WM_IME_CONTROL query for ImeChk's per-tick polling.
        // Returns false on failure/timeout; caller keeps the previously shown state.
        private bool TryImeChkSendMessage(IntPtr hImeWnd, uint wParam, out IntPtr result)
        {
            result = IntPtr.Zero;
            return SendMessageTimeout(hImeWnd, WM_IME_CONTROL, (IntPtr)wParam, (IntPtr)0,
                SMTO_ABORTIFHUNG, IME_CHK_TIMEOUT_MS, ref result) != 0;
        }

        private void ImeChk_Event(object sender, EventArgs args)
        {
            ((DispatcherTimer)sender).Stop();
            ImeChk();
            ((DispatcherTimer)sender).Start();
        }

        private void ImeChk()
        {
            string NewImmTip;
            IntPtr hImeWnd;
            uint NewInputMode;
            int ImeOpenStatus;
            uint NewInputFlag;
            uint NewImmConversion;
            bool NewInputRoma;
            string NewImeStatus;
            bool NeedDspUpdate;

            const int MODE_FULL_SHAPE_HIRAGANA = 40000;
            const int MODE_FULL_SHAPE_KATAKANA = 40001;
            const int MODE_FULL_SHAPE_ALPHANUMERIC = 40002;
            const int MODE_KANA = 40003;
            const int MODE_ALPHANUMERIC = 40004;
            const int MODE_DIRECT = 40005;

            UpdateOtherTopWindow();

            NeedDspUpdate = false;
            NewImeStatus = @"✖";
            NewImmTip = (string)this.FindResource("ime_input_tip_disabled");

            if ((hImeWnd = ImmGetDefaultIMEWnd(hWndCurFg)) == (IntPtr)0)
                NewImeEnabled = false;  // IME window not found

            if (NewImeEnabled)
            {
                NewImmTip = (string)this.FindResource("ime_input_tip_enabled");

                NewInputMode = 0;

                if (!TryImeChkSendMessage(hImeWnd, IMC_GETOPENSTATUS, out IntPtr openStatusResult))
                    return;     // IME window not responding; keep previously shown state
                ImeOpenStatus = (int)openStatusResult;

                if (ImeOpenStatus != 0)
                {
                    // conversion enabled
                    if (!TryImeChkSendMessage(hImeWnd, IMC_GETCONVERSIONMODE, out IntPtr conversionModeResult))
                        return;     // IME window not responding; keep previously shown state
                    NewInputFlag = (uint)conversionModeResult;

                    if (!TryImeChkSendMessage(hImeWnd, IMC_GETSENTENCEMODE, out IntPtr sentenceModeResult))
                        return;     // IME window not responding; keep previously shown state
                    NewImmConversion = (uint)sentenceModeResult;

                    if (GetRegKanaMd())
                    {
                        // Kana Input
                        NewInputRoma = false;
                    }
                    else
                    {
                        // Romanized Input
                        NewInputRoma = true;
                    }

                    if ((NewInputFlag & IME_CMODE_FULLSHAPE) != 0)
                    {
                        // full shape
                        if ((NewInputFlag & IME_CMODE_NATIVE) != 0)
                        {
                            // full shape / japanese
                            if ((NewInputFlag & IME_CMODE_KATAKANA) != 0)
                            {
                                // full shape / katakana
                                NewInputMode = MODE_FULL_SHAPE_KATAKANA;
                                NewImeStatus = @"カ";
                            }
                            else
                            {
                                // full shape / hiragana
                                NewInputMode = MODE_FULL_SHAPE_HIRAGANA;
                                NewImeStatus = @"あ";
                            }
                        }
                        else
                        {
                            // full shape / alphanumeric
                            NewInputMode = MODE_FULL_SHAPE_ALPHANUMERIC;
                            NewImeStatus = @"Ａ";
                        }
                    }
                    else
                    {
                        // nornal shape
                        if ((NewInputFlag & IME_CMODE_KATAKANA) != 0)
                        {
                            // nornal shape / katakana
                            NewInputMode = MODE_KANA;
                            // If type it directly, the editor will become confused because it contains special characters.
                            NewImeStatus = "\u033A\uFF76\u0000";
                        }
                        else
                        {
                            // nornal shape / alphanumeric
                            NewInputMode = MODE_ALPHANUMERIC;
                            // If type it directly, the editor will become confused because it contains special characters.
                            NewImeStatus = "\u033A\u0041\u0000";
                        }
                    }
                }
                else
                {
                    // conversion disabled
                    NewInputRoma = false;
                    NewImmConversion = 0;
                    NewInputMode = MODE_DIRECT;
                    NewImeStatus = "A";
                }

                if (CurInputRoma != NewInputRoma)
                {
                    CurInputRoma = NewInputRoma;
                    NeedDspUpdate = true;
                }

                if (CurInputMode != NewInputMode)
                {
                    CurInputMode = NewInputMode;
                    NeedDspUpdate = true;
                }

                if (CurImmConversion != NewImmConversion)
                {
                    CurImmConversion = NewImmConversion;
                    NeedDspUpdate = true;
                }
            }

            if (JapaneseImeEnabled != NewImeEnabled)
            {
                JapaneseImeEnabled = NewImeEnabled;
                NeedDspUpdate = true;
            }

            if (JapaneseImeTip != NewImmTip)
            {
                JapaneseImeTip = NewImmTip;
                NeedDspUpdate = true;
            }

            if (JapaneseImeStatus != NewImeStatus)
            {
                JapaneseImeStatus = NewImeStatus;
                NeedDspUpdate = true;
            }

            if (NeedDspUpdate)
            {
                JapaneseIme_full_shape_hiragana.IsChecked = CurInputMode == MODE_FULL_SHAPE_HIRAGANA;
                JapaneseIme_full_shape_katakana.IsChecked = CurInputMode == MODE_FULL_SHAPE_KATAKANA;
                JapaneseIme_full_shape_alphanumeric.IsChecked = CurInputMode == MODE_FULL_SHAPE_ALPHANUMERIC;
                JapaneseIme_kana.IsChecked = CurInputMode == MODE_KANA;
                JapaneseIme_alphanumeric.IsChecked = CurInputMode == MODE_ALPHANUMERIC;
                JapaneseIme_direct.IsChecked = CurInputMode == MODE_DIRECT;

                if (CurInputMode != MODE_DIRECT)
                {
                    JapaneseIme_input_key_roma.IsChecked = CurInputRoma;
                    JapaneseIme_input_key_kana.IsChecked = !CurInputRoma;
                    JapaneseIme_conversion_general.IsChecked = CurImmConversion != 0;
                    JapaneseIme_conversion_none.IsChecked = CurImmConversion == 0;
                }
                else
                {
                    JapaneseIme_input_key_roma.IsChecked = false;
                    JapaneseIme_input_key_kana.IsChecked = false;
                    JapaneseIme_conversion_general.IsChecked = false;
                    JapaneseIme_conversion_none.IsChecked = false;
                }
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
            ImeCheckTimer.Stop();

            _isLoaded = false;
        }
  };
}