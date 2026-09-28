using ManagedShell.Interop;
using System;
using System.Collections.Concurrent;

namespace ManagedShell.Common.Native
{
    // UltraWinBar (K12): factory for delegates that user32 calls back into directly (WNDPROC,
    // EnumWindows). Roots the wrapper for the process lifetime (native code must never call into a
    // collected thunk), reports an escaping exception via CallbackGuard instead of letting it reach
    // native code, and counts calls per name. WinEventHook is its own equivalent for hook procs.
    internal static class NativeCallback
    {
        private static readonly ConcurrentBag<Delegate> rooted = new ConcurrentBag<Delegate>();
        private static readonly ConcurrentDictionary<string, long> counts = new ConcurrentDictionary<string, long>();

        internal static long CallCount(string name) => counts.TryGetValue(name, out long count) ? count : 0;

        // WNDPROC-shaped: on an escaping exception, report and fall back to DefWindowProc so the
        // message loop still gets a well-formed reply.
        internal static NativeMethods.WndProcDelegate Wrap(string name, NativeMethods.WndProcDelegate handler)
        {
            NativeMethods.WndProcDelegate wrapped = (hWnd, msg, wParam, lParam) =>
            {
                counts.AddOrUpdate(name, 1, (_, c) => c + 1);
                try { return handler(hWnd, msg, wParam, lParam); }
                catch (Exception error)
                {
                    CallbackGuard.Report(name, error);
                    return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
                }
            };
            rooted.Add(wrapped);
            return wrapped;
        }

        // EnumWindows/EnumChildWindows-shaped: on an escaping exception, report and stop enumeration.
        // Not rooted: enumeration is synchronous, and the argument stays alive for the call.
        internal static NativeMethods.CallBackPtr Wrap(string name, NativeMethods.CallBackPtr handler)
        {
            NativeMethods.CallBackPtr wrapped = (hwnd, lParam) =>
            {
                counts.AddOrUpdate(name, 1, (_, c) => c + 1);
                try { return handler(hwnd, lParam); }
                catch (Exception error)
                {
                    CallbackGuard.Report(name, error);
                    return false;
                }
            };
            return wrapped;
        }
    }
}
