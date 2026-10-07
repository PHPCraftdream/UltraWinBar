using System;
using System.Runtime.InteropServices;

namespace ManagedShell.WindowsTasks
{
    public static class WindowGhosting
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowMapping(IntPtr window);

        private static readonly IntPtr User32 = NativeLibrary.Load("user32.dll");
        private static readonly WindowMapping FindOriginal = Load("HungWindowFromGhostWindow");
        private static readonly WindowMapping FindGhost = Load("GhostWindowFromHungWindow");

        private static WindowMapping Load(string name) => NativeLibrary.TryGetExport(User32, name, out var address)
            ? Marshal.GetDelegateForFunctionPointer<WindowMapping>(address) : null;

        public static IntPtr Original(IntPtr window)
        {
            IntPtr original = FindOriginal?.Invoke(window) ?? IntPtr.Zero;
            return original != IntPtr.Zero ? original : window;
        }

        internal static IntPtr Surface(IntPtr window)
        {
            IntPtr ghost = FindGhost?.Invoke(window) ?? IntPtr.Zero;
            return ghost != IntPtr.Zero ? ghost : window;
        }
    }
}
