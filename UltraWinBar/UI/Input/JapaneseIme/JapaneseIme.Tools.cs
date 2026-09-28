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
        private IntPtr ImmOpenGetWindow()
        {
            IntPtr hImeWnd;
            int OpenSts;

            hImeWnd = ImmSetOpen(ImmOpenStatus.ImmOpen);

            OpenSts = (int)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, (IntPtr)0);

            if (OpenSts == 0)
                return (IntPtr)0;

            return hImeWnd;
        }

        private enum ImmOpenStatus
        {
            ImmClose,
            ImmOpen,
            ImmToggle
        }

        private IntPtr ImmSetOpen(ImmOpenStatus SetStatus)
        {
            IntPtr hImeWnd;
            int OpenCurSts;
            int OpenNewSts;

            SetForeOtherTopWindow();

            if ((hImeWnd = ImmGetDefaultIMEWnd(hWndCurFg)) == (IntPtr)0)
                return (IntPtr)0;

            OpenCurSts = (int)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, (IntPtr)0);

            switch (SetStatus)
            {
                case ImmOpenStatus.ImmOpen:
                    OpenNewSts = 1;
                    break;

                case ImmOpenStatus.ImmClose:
                    OpenNewSts = 0;
                    break;

                case ImmOpenStatus.ImmToggle:
                    OpenNewSts = OpenCurSts == 0 ? 1 : 0;
                    break;

                default:
                    OpenNewSts = OpenCurSts;
                    break;
            }

            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETOPENSTATUS, (IntPtr)OpenNewSts);

            return hImeWnd;
        }

        // When changing the IME state from the UltraWinBar,
        // focus should be returned to the window the user was previously using.
        private void SetForeOtherTopWindow()
        {
            UpdateOtherTopWindow();

            if (hWndCurFg == GetForegroundWindow())
                return;     // no need to execute

            if (!IsWindow(hWndCurFg))
                return;     // invalid Window

            if (!IsWindowVisible(hWndCurFg))
                return;     // hidden Window

            SetForegroundWindow(hWndCurFg);
            Thread.Sleep(200);  // 100ms = OK / 50ms = NG
            return;
        }

        // IME state is different for each window.
        // Clicking will give focus to UltraWinBar.
        // Need to get the previous window.
        private void UpdateOtherTopWindow()
        {
            IntPtr hWkTop;
            uint WkThId, WkProcId;
            int WkLayout;

            if ((hWkTop = GetForegroundWindow()) == (IntPtr)0)
                return; // error, not update.

            if ((WkThId = GetWindowThreadProcessId(hWkTop, out WkProcId)) == 0)
                return; // error, not update.

            if (WkProcId == GetCurrentProcessId())
                return;	// foreground is current process. not update.

            // Reference win32api.
            //     MAKELANGID(LANG_JAPANESE, SUBLANG_JAPANESE_JAPAN) == 0x411
            WkLayout = GetKeyboardLayout(WkThId);
            NewImeEnabled = (WkLayout & 0xFFFF) == 0x411;

            hWndCurFg = hWkTop; // update
            return;
        }

        private bool ExecImeDictionaryTool(string ExecOpt)
        {
            string WinDirPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string Dict32Name = WinDirPath + @"\SysWOW64\IME\IMEJP\imjpdct.exe";
            string Dict64Name = WinDirPath + @"\System32\IME\IMEJP\imjpdct.exe";

            SetForeOtherTopWindow();

            return PrgExec(Dict32Name, Dict64Name, ExecOpt);
        }

        private bool ExecImeProperties(string ExecOpt)
        {
            string WinDirPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string Prop32Name = WinDirPath + @"\SysWOW64\IME\IMEJP\imjpset.exe";
            string Prop64Name = WinDirPath + @"\System32\IME\IMEJP\imjpset.exe";

            SetForeOtherTopWindow();

            return PrgExec(Prop32Name, Prop64Name, ExecOpt);
        }

        // Ideally, call it via an API,
        // but this is a suboptimal solution.
        private bool ExecImePad(string ExecOpt)
        {
            bool RetSts = true;

            INPUT[] InBuf = new INPUT[6];
            uint RetCnt;

            try
            {
                // [Ctrl] + [F10]
                InBuf[0].type = INPUT_KEYBOARD;
                InBuf[0].mkhi.ki.wVk = VK_CONTROL;
                InBuf[0].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

                InBuf[1].type = INPUT_KEYBOARD;
                InBuf[1].mkhi.ki.wVk = VK_F10;
                InBuf[1].mkhi.ki.dwFlags = 0;

                InBuf[2].type = INPUT_KEYBOARD;
                InBuf[2].mkhi.ki.wVk = VK_F10;
                InBuf[2].mkhi.ki.dwFlags = KEYEVENTF_KEYUP;

                InBuf[3].type = INPUT_KEYBOARD;
                InBuf[3].mkhi.ki.wVk = VK_CONTROL;
                InBuf[3].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

                // [P]
                InBuf[4].type = INPUT_KEYBOARD;
                InBuf[4].mkhi.ki.wVk = 'P';
                InBuf[4].mkhi.ki.dwFlags = 0;

                InBuf[5].type = INPUT_KEYBOARD;
                InBuf[5].mkhi.ki.wVk = 'P';
                InBuf[5].mkhi.ki.dwFlags = KEYEVENTF_KEYUP;

                RetCnt = SendInput(6, InBuf, Marshal.SizeOf(typeof(INPUT)));

                if (RetCnt != 6)
                    throw new System.Exception();
            }

            catch
            {
                RetSts = false;
            }

            return RetSts;
        }

        private bool PrgExec(string Exec32bit, string Exec64bit, string ExecOpt)
        {
            bool RetSts = true;
            bool Wow64Sts;

            if (!IsWow64Process(GetCurrentProcess(), out Wow64Sts))
                return false;

            if (Wow64Sts)
                RetSts = ManagedShell.Common.Helpers.ShellHelper.StartProcess(Exec32bit);     // 32bit
            else
                RetSts = ManagedShell.Common.Helpers.ShellHelper.StartProcess(Exec64bit);     // 64bit

            return RetSts;
        }

        // Determining whether input is kana or romaji
        // does not work via the API and requires reading the registry.
        private bool GetRegKanaMd()
        {
            bool RetMode;

            string RegKeyStrKanaMd = @"Software\AppDataLow\Software\Microsoft\IME\15.0\IMEJP\MSIME";
            int ChkWk;

            UIntPtr hKey;

            uint GetType, GetData;
            uint GetLen = REG_SIZE_DWORD;

            unchecked
            {
                if ((ChkWk = RegOpenKey((UIntPtr)RegistryHive.CurrentUser, RegKeyStrKanaMd, out hKey)) != ERROR_SUCCESS)
                    return false;   // If unknown, treat as "roma"
            }

            ChkWk = RegQueryValueEx(hKey, "kanaMd", (IntPtr)0, out GetType, out GetData, ref GetLen);

            RegCloseKey(hKey);

            if (ChkWk == ERROR_SUCCESS && GetType == REG_DWORD)
            {
                if (GetData == 1)
                    RetMode = true;		// 1: kana
                else
                    RetMode = false;	// 2: roma
            }
            else
            {
                RetMode = true;		// If unknown, treat as "roma"
            }

            return RetMode;
        }

        // Switching between Kana input and Romaji input
        // affects all windows simultaneously.
        private bool ToggleKanaMode()
        {
            bool RetSts = true;

            INPUT[] InBuf = new INPUT[6];
            uint RetCnt;

            try
            {
                // [Ctrl] + [Shift] + [kana]
                InBuf[0].type = INPUT_KEYBOARD;
                InBuf[0].mkhi.ki.wVk = VK_CONTROL;
                InBuf[0].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

                InBuf[1].type = INPUT_KEYBOARD;
                InBuf[1].mkhi.ki.wVk = VK_SHIFT;
                InBuf[1].mkhi.ki.dwFlags = 0;

                // https://atmarkit.itmedia.co.jp/bbs/phpBB/viewtopic.php?topic=42587&forum=7
                //	VK_OEM_COPY = 0xF2,		// kana key button.

                InBuf[2].type = INPUT_KEYBOARD;
                InBuf[2].mkhi.ki.wVk = VK_OEM_COPY;     // kana key button.
                InBuf[2].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

                InBuf[3].type = INPUT_KEYBOARD;
                InBuf[3].mkhi.ki.wVk = VK_OEM_COPY;     // kana key button.
                InBuf[3].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

                InBuf[4].type = INPUT_KEYBOARD;
                InBuf[4].mkhi.ki.wVk = VK_SHIFT;
                InBuf[4].mkhi.ki.dwFlags = KEYEVENTF_KEYUP;

                InBuf[5].type = INPUT_KEYBOARD;
                InBuf[5].mkhi.ki.wVk = VK_CONTROL;
                InBuf[5].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

                RetCnt = SendInput(6, InBuf, Marshal.SizeOf(typeof(INPUT)));

                if (RetCnt != 6)
                    throw new System.Exception();
            }

            catch
            {
                RetSts = false;
            }

            return RetSts;
        }
    }
}
