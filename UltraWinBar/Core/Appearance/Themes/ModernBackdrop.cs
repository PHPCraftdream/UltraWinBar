using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ManagedShell.Common.Helpers;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The blurred desktop behind a window of the Modern Glass theme. DWM blurs what is behind the window and the
    /// window's own translucent colors lie over it, so the program paints nothing extra and the window is not
    /// a layered one (no AllowsTransparency, which WPF draws slowly): the client area is made part of the frame,
    /// where DWM honors the alpha of what WPF draws. The plain blur (not the acrylic one) is used: it does not
    /// stutter when the window is dragged.
    /// </summary>
    internal static class ModernBackdrop
    {
        private const int AccentDisabled = 0;
        private const int AccentBlurBehind = 3;
        private const int AccentPolicyAttribute = 19;

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left, Right, Top, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);

        // Set when DWM refused the blur on some window: the windows then paint themselves opaque.
        internal static bool Failed { get; private set; }

        // The user's "blur behind" setting, under the theme, on a Windows that has the blur (10 version 1803 and later).
        internal static bool PanelWanted => ModernTheme.IsActive && Settings.Instance.AllowBlurBehind && EnvironmentHelper.IsWindows10RS4OrBetter;

        internal static bool Wanted => PanelWanted && !Failed;

        // True when the window now has (or no longer has) the blur as asked.
        internal static bool Set(Window window, bool on)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            try
            {
                if (on && HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
                int edge = on ? -1 : 0;
                var margins = new Margins { Left = edge, Right = edge, Top = edge, Bottom = edge };
                int framed = DwmExtendFrameIntoClientArea(handle, ref margins);
                return SetAccent(handle, on ? AccentBlurBehind : AccentDisabled) && (framed == 0 || !on);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }

        // The blur could not be set: from now on the windows are opaque.
        internal static void MarkFailed()
        {
            Failed = true;
        }

        private static bool SetAccent(IntPtr handle, int state)
        {
            var policy = new AccentPolicy { AccentState = state };
            int size = Marshal.SizeOf(policy);
            IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, memory, false);
                var data = new CompositionData { Attribute = AccentPolicyAttribute, Data = memory, SizeOfData = size };
                return SetWindowCompositionAttribute(handle, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }
    }
}
