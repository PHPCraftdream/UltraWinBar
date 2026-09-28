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
    public partial class JapaneseIme
    {
        private void JapaneseIme_OnClick(object sender, RoutedEventArgs e)
        {
            ImmSetOpen(ImmOpenStatus.ImmToggle);
        }

        private void JapaneseIme_full_shape_hiragana_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion |= IME_CMODE_FULLSHAPE;
            ImmNewConversion |= IME_CMODE_NATIVE;
            ImmNewConversion &= ~IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_full_shape_katakana_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion |= IME_CMODE_FULLSHAPE;
            ImmNewConversion |= IME_CMODE_NATIVE;
            ImmNewConversion |= IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_full_shape_alphanumeric_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion |= IME_CMODE_FULLSHAPE;
            ImmNewConversion &= ~IME_CMODE_NATIVE;
            ImmNewConversion &= ~IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_kana_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion &= ~IME_CMODE_FULLSHAPE;
            ImmNewConversion |= IME_CMODE_NATIVE;
            ImmNewConversion |= IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_alphanumeric_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion &= ~IME_CMODE_FULLSHAPE;
            ImmNewConversion &= ~IME_CMODE_NATIVE;
            ImmNewConversion &= ~IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_direct_OnClick(object sender, RoutedEventArgs e)
        {
            ImmSetOpen(ImmOpenStatus.ImmClose);
        }

        private void JapaneseIme_open_imepad_OnClick(object sender, RoutedEventArgs e)
        {
            if (ImmOpenGetWindow() == (IntPtr)0)
                return;

            ExecImePad("");
        }

        private void JapaneseIme_open_add_word_dictionary_OnClick(object sender, RoutedEventArgs e)
        {
            ExecImeDictionaryTool("-w");
        }

        private void JapaneseIme_open_dictionary_tool_OnClick(object sender, RoutedEventArgs e)
        {
            ExecImeDictionaryTool("-t");
        }

        private void JapaneseIme_open_properties_OnClick(object sender, RoutedEventArgs e)
        {
            ExecImeProperties("");
        }

        private void JapaneseIme_input_key_roma_OnClick(object sender, RoutedEventArgs e)
        {
            if (ImmOpenGetWindow() == (IntPtr)0)
                return;

            if (CurInputRoma)
                return;     // no need to execute

            ToggleKanaMode();	// change "kana" to "roma"
        }

        private void JapaneseIme_input_key_kana_OnClick(object sender, RoutedEventArgs e)
        {
            if (ImmOpenGetWindow() == (IntPtr)0)
                return;

            if (!CurInputRoma)
                return;     // no need to execute

            ToggleKanaMode();	// change "roma" to "kana"
        }

        private void JapaneseIme_conversion_general_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETSENTENCEMODE, (IntPtr)0x08);
        }

        private void JapaneseIme_conversion_none_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETSENTENCEMODE, (IntPtr)0x00);
        }
    }
}
