using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    // Hosts a Win32 hook on a dedicated thread with its own message loop, so a slow UI thread never
    // delays delivery of the hook to the rest of the system. Install/uninstall are injectable for testing.
    internal sealed class DesktopActivationHookThread : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const uint WM_QUIT = 0x0012;
        private const int JoinTimeoutMs = 2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        private readonly Thread thread;
        private volatile uint hookThreadId;
        private bool disposed;

        internal DesktopActivationHookThread(Func<bool> install, Action uninstall)
        {
            var ready = new ManualResetEventSlim(false);
            bool installed = false;
            Exception failure = null;
            thread = new Thread(() =>
            {
                // Create the message queue before publishing the thread id, or an early WM_QUIT is silently dropped.
                PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
                hookThreadId = GetCurrentThreadId();
                try { installed = install(); }
                catch (Exception error) { failure = error; }
                finally { ready.Set(); }
                if (!installed) return;
                try
                {
                    int result;
                    while ((result = GetMessage(out MSG msg, IntPtr.Zero, 0, 0)) > 0)
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                    if (result < 0) ShellLogger.Warning("DesktopActivation: hook thread message loop failed.");
                }
                finally
                {
                    try { uninstall(); }
                    catch (Exception error) { ShellLogger.Warning($"DesktopActivation: hook thread teardown failed: {error.Message}"); }
                }
            })
            { IsBackground = true, Name = "UltraWinBar Desktop Activation Hook" };
            thread.Start();
            // No timeout: giving up early would leave a late-installed hook running with no owner.
            ready.Wait();
            ready.Dispose();
            if (failure != null) throw new InvalidOperationException("Desktop activation hook installation failed.", failure);
            if (!installed) throw new InvalidOperationException("Desktop activation hook installation failed.");
        }

        public void Dispose()
        {
            if (Stop() && Thread.CurrentThread != thread) thread.Join(JoinTimeoutMs);
        }

        // Without waiting: for a hook whose handler calls into another thread that may be busy (or be the caller).
        internal bool Stop()
        {
            if (disposed) return false;
            disposed = true;
            uint id = hookThreadId;
            if (id != 0) PostThreadMessage(id, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            return true;
        }
    }
}
