using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UltraWinBar.Utilities
{
    // The only way the app installs WinEvent hooks. Installed hooks (and their callback thunks) stay
    // rooted even if an owner forgets Dispose, unhooking happens on the installing thread, and handler
    // exceptions never reach user32 (an escaping exception there kills the process).
    internal sealed class WinEventHook : IDisposable
    {
        internal const uint OutOfContext = 0x0000;
        internal const uint SkipOwnProcess = 0x0002;

        internal delegate void Handler(uint eventType, IntPtr hwnd, int idObject, int idChild);
        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static readonly HashSet<WinEventHook> installed = new HashSet<WinEventHook>();
        private readonly string name;
        private readonly Handler handler;
        private readonly WinEventProc callback;
        private readonly uint ownerThread;
        private IntPtr hook;

        internal static int InstalledCount { get { lock (installed) return installed.Count; } }

        internal WinEventHook(string name, uint eventMin, uint eventMax, Handler handler, uint flags = OutOfContext)
        {
            this.name = name;
            this.handler = handler;
            callback = OnEvent;
            ownerThread = GetCurrentThreadId();
            hook = SetWinEventHook(eventMin, eventMax, IntPtr.Zero, callback, 0, 0, flags);
            if (hook != IntPtr.Zero) { lock (installed) installed.Add(this); }
            else ManagedShell.Common.Logging.ShellLogger.Error($"{name}: SetWinEventHook failed ({Marshal.GetLastWin32Error()}).");
        }

        internal bool IsInstalled => hook != IntPtr.Zero;

        private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (this.hook == IntPtr.Zero) return;
            try { handler(eventType, hwnd, idObject, idChild); }
            catch (Exception error) { CallbackGuard.Report(name, error); }
        }

        // Safe inside this hook's own out-of-context callback: it only stops future dispatch.
        public void Dispose()
        {
            if (hook == IntPtr.Zero) return;
            Debug.Assert(GetCurrentThreadId() == ownerThread, $"{name}: WinEvent hooks must be removed on the installing thread.");
            UnhookWinEvent(hook);
            hook = IntPtr.Zero;
            lock (installed) installed.Remove(this);
        }
    }
}
