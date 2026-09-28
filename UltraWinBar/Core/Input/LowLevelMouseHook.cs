using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ManagedShell.Common.Logging;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    // TODO: This should move to ManagedShell
    public class LowLevelMouseHook : IDisposable
    {
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProcDelegate callback, IntPtr hInstance, uint threadId);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public delegate IntPtr LowLevelMouseProcDelegate(int code, IntPtr wParam, IntPtr lParam);

        const int WH_MOUSE_LL = 14;

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public class LowLevelMouseEventArgs : HandledEventArgs
        {
            public WM Message;
            public MSLLHOOKSTRUCT HookStruct;
        }

        public event EventHandler<LowLevelMouseEventArgs> LowLevelMouseEvent;

        // Installed hooks stay rooted: an owner dropped without Dispose must not let GC free the
        // callback thunk while Windows still calls it (access violation in unknown code).
        private static readonly HashSet<LowLevelMouseHook> _installed = new HashSet<LowLevelMouseHook>();

        internal static int InstalledCount { get { lock (_installed) return _installed.Count; } }

        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProcDelegate _hookDelegate;

        public LowLevelMouseHook() {
            _hookDelegate = MouseHookProc;
        }

        public bool Initialize()
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookDelegate, GetModuleHandle(curModule.ModuleName), 0);

                if (_hook == IntPtr.Zero)
                {
                    return false;
                }

                lock (_installed) _installed.Add(this);
                return true;
            }
        }

        // Test seam: exercises the exact marshaling/dispatch path a real hook call uses, without
        // SetWindowsHookEx or SendInput. Builds a real native buffer so PtrToStructure<T> is exercised
        // end-to-end, not just the managed copy. code=0 (HC_ACTION) so it flows like a genuine event.
        internal IntPtr TestDispatch(WM message, MSLLHOOKSTRUCT hookStruct)
        {
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
            try
            {
                Marshal.StructureToPtr(hookStruct, buffer, false);
                return MouseHookProc(0, (IntPtr)(uint)message, buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0)
            {
                return CallNextHookEx(_hook, code, wParam, lParam);
            }

            LowLevelMouseEventArgs args = new LowLevelMouseEventArgs
            {
                Message = (WM)(uint)wParam.ToInt64(),
                HookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam)
            };

            try
            {
                LowLevelMouseEvent?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                try { ShellLogger.Error($"LowLevelMouseHook callback failed: {ex.Message}"); }
                catch { }
            }

            if (args.Handled)
            {
                return (IntPtr)1;
            }

            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            lock (_installed) _installed.Remove(this);
        }
    }
}
