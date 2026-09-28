using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace ManagedShell.Common.Native
{
    // UltraWinBar (K12): moved from the app so ManagedShell's own WinEvent hooks (tray, tasks,
    // ExplorerHelper) share the same primitive instead of calling SetWinEventHook directly.
    // The only way to install WinEvent hooks. Installed hooks (and their callback thunks) stay
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
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static readonly HashSet<WinEventHook> installed = new HashSet<WinEventHook>();
        private readonly string name;
        private readonly Handler handler;
        private readonly WinEventProc callback;
        private readonly uint ownerThread;
        private readonly Dispatcher ownerDispatcher;
        private IntPtr hook;

        internal static int InstalledCount { get { lock (installed) return installed.Count; } }

        internal WinEventHook(string name, uint eventMin, uint eventMax, Handler handler, uint flags = OutOfContext)
        {
            this.name = name;
            this.handler = handler;
            callback = OnEvent;
            ownerThread = GetCurrentThreadId();
            ownerDispatcher = Dispatcher.FromThread(Thread.CurrentThread);
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
        // UnhookWinEvent must run on the installing thread. Off-thread Dispose marshals the actual
        // unhook there via its Dispatcher; until that runs, this object stays in `installed` (rooted)
        // so user32 never calls into a collected thunk.
        public void Dispose()
        {
            if (hook == IntPtr.Zero) return;
            if (GetCurrentThreadId() == ownerThread) { Unhook(); return; }

            if (ownerDispatcher != null && !ownerDispatcher.HasShutdownStarted && !ownerDispatcher.HasShutdownFinished)
            {
                ownerDispatcher.BeginInvoke(new Action(Unhook));
            }
            else
            {
                ManagedShell.Common.Logging.ShellLogger.Error(
                    $"{name}: Dispose called off the installing thread with no reachable Dispatcher; keeping the hook and its delegate rooted to avoid a dangling thunk.");
            }
        }

        private void Unhook()
        {
            if (hook == IntPtr.Zero) return;
            Debug.Assert(GetCurrentThreadId() == ownerThread, $"{name}: WinEvent hooks must be removed on the installing thread.");
            if (UnhookWinEvent(hook))
            {
                hook = IntPtr.Zero;
                lock (installed) installed.Remove(this);
            }
            else
            {
                ManagedShell.Common.Logging.ShellLogger.Error($"{name}: UnhookWinEvent failed ({Marshal.GetLastWin32Error()}); keeping the delegate rooted.");
            }
        }
    }
}
