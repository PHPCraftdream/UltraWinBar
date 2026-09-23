using ManagedShell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace UltraWinBar.Utilities
{
    internal static class WorkAreaManager
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam,
            string lParam, uint flags, uint timeout, out IntPtr result);

        public static void Apply(NativeMethods.Rect workArea, uint UltraWinBarProcessId)
        {
            NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.SETWORKAREA, 0, ref workArea, 0);

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == UltraWinBarProcessId)
                {
                    return true;
                }

                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                StringBuilder className = new StringBuilder(64);
                GetClassName(hWnd, className, className.Capacity);
                if (className.ToString() == "Shell_TrayWnd" || className.ToString() == "Shell_SecondaryTrayWnd")
                {
                    return true;
                }

                SendMessageTimeout(hWnd, (uint)NativeMethods.WM.SETTINGCHANGE,
                    (IntPtr)NativeMethods.SPI.SETWORKAREA, "WorkArea", 0x2, 250, out _);
                return true;
            }, IntPtr.Zero);
        }
    }
}
