using ManagedShell.Common.Logging;
using ManagedShell.Common.Native;
using System;
using System.Runtime.InteropServices;
using System.Text;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    // Keeps Open Shell's avatar transparent until it is placed beside the moved menu. Open Shell shows it next to
    // where it first put the menu, and moving it (SetWindowPos) waits for Explorer's thread, so it flashed there.
    // The avatar is per-pixel layered: UpdateLayeredWindow without a source changes only its alpha, at once, from
    // any process; it runs on a hook thread so it follows Open Shell's own updates (which reset the alpha) with no UI lag.
    internal sealed class StartMenuAvatarGuard : IDisposable
    {
        private const uint EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint ULW_ALPHA = 2;
        private const byte AC_SRC_ALPHA = 1;
        private const int THREAD_PRIORITY_TIME_CRITICAL = 15;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")]
        private static extern bool SetThreadPriority(IntPtr thread, int priority);

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize, IntPtr hdcSrc,
            IntPtr pptSrc, uint key, ref BlendFunction blend, uint flags);

        private readonly Func<IntPtr, bool> _isAvatar;
        private readonly Action<IntPtr, byte> _setAlpha;
        private readonly object _gate = new object();
        private DesktopActivationHookThread _thread;
        private IntPtr _avatar;
        private bool _placed;
        private byte _alpha;
        // Each UpdateLayeredWindow raises one LOCATIONCHANGE; re-hiding on that echo would loop.
        private int _echoes;
        private (bool Visible, int Left, int Top, int Right, int Bottom) _hiddenState;

        internal StartMenuAvatarGuard(Func<IntPtr, bool> isAvatar = null, Action<IntPtr, byte> setAlpha = null)
        {
            _isAvatar = isAvatar ?? IsOpenShellAvatar;
            _setAlpha = setAlpha ?? SetAlpha;
        }

        internal bool IsArmed => _thread != null;

        internal static void SetAlpha(IntPtr hwnd, byte alpha)
        {
            var blend = new BlendFunction { SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, ULW_ALPHA);
        }

        private static bool IsOpenShellAvatar(IntPtr hwnd)
        {
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString() == "OpenShell.CUserWindow";
        }

        // menu: Open Shell creates the avatar on the menu's thread, so only that thread's events are hooked.
        internal void Arm(IntPtr menu)
        {
            Disarm();
            uint thread = GetWindowThreadProcessId(menu, out uint process);
            if (thread == 0) return;
            lock (_gate)
            {
                _avatar = IntPtr.Zero;
                _placed = false;
                _alpha = 0;
                _echoes = 0;
                _hiddenState = default;
            }
            WinEventHook show = null, location = null;
            try
            {
                _thread = new DesktopActivationHookThread(() =>
                {
                    // Open Shell resets the alpha right before showing the avatar; the answer must beat the next frame.
                    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
                    show = new WinEventHook("Start menu avatar show", EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, OnEvent,
                        WinEventHook.OutOfContext, process, thread);
                    location = new WinEventHook("Start menu avatar location", EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, OnEvent,
                        WinEventHook.OutOfContext, process, thread);
                    return show.IsInstalled && location.IsInstalled;
                }, () =>
                {
                    show?.Dispose();
                    location?.Dispose();
                });
                // Open Shell creates the avatar before the menu takes focus: hide an existing one before it is shown.
                IntPtr existing = IntPtr.Zero;
                while ((existing = FindWindowEx(IntPtr.Zero, existing, "OpenShell.CUserWindow", IntPtr.Zero)) != IntPtr.Zero)
                {
                    if (GetWindowThreadProcessId(existing, out _) != thread || IsWindowVisible(existing)) continue;
                    lock (_gate)
                    {
                        if (_avatar != IntPtr.Zero) break;
                        _avatar = existing;
                        HideLocked(existing);
                    }
                    break;
                }
            }
            catch (InvalidOperationException error)
            {
                _thread = null;
                ShellLogger.Warning($"StartMenuAvatarGuard: {error.Message}");
            }
        }

        // Hook thread.
        private void OnEvent(uint type, IntPtr hwnd, int obj, int child)
        {
            if (obj != 0 || child != 0) return;
            lock (_gate)
            {
                if (_placed || (_avatar != IntPtr.Zero ? hwnd != _avatar : !_isAvatar(hwnd))) return;
                _avatar = hwnd;
                // Only an unchanged window is our echo: Open Shell moving or showing it (and resetting the alpha)
                // right after our change must be answered at once, not when its SHOW arrives.
                if (type == EVENT_OBJECT_LOCATIONCHANGE && _echoes > 0 && State(hwnd) == _hiddenState)
                {
                    _echoes--;
                    return;
                }
                HideLocked(hwnd);
            }
        }

        private void HideLocked(IntPtr hwnd)
        {
            _setAlpha(hwnd, 0);
            _echoes++;
            _hiddenState = State(hwnd);
        }

        private static (bool Visible, int Left, int Top, int Right, int Bottom) State(IntPtr hwnd)
        {
            GetWindowRect(hwnd, out ManagedShell.Interop.NativeMethods.Rect rect);
            return (IsWindowVisible(hwnd), rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        // UI thread, once the avatar is at its place beside the menu.
        internal void MarkPlaced(IntPtr avatar)
        {
            lock (_gate)
            {
                if (_placed || _thread == null) return;
                _avatar = avatar;
                _placed = true;
                _setAlpha(avatar, _alpha);
            }
            if (_alpha == 255) Disarm();
        }

        // UI thread, with the menu's fade alpha; at 255 the avatar is shown even if it was never placed.
        internal void Show(byte alpha)
        {
            lock (_gate)
            {
                if (_thread == null) return;
                _alpha = alpha;
                if (alpha == 255) _placed = true;
                if (_placed && _avatar != IntPtr.Zero) _setAlpha(_avatar, alpha);
            }
            if (alpha == 255) Disarm();
        }

        // Closed, dropped or done: a still-hidden avatar is made opaque again.
        internal void Disarm()
        {
            if (_thread == null) return;
            // Not joined: the hook thread unhooks and ends itself.
            _thread.Stop();
            _thread = null;
            lock (_gate)
            {
                if (_avatar != IntPtr.Zero && (!_placed || _alpha != 255) && IsWindow(_avatar)) _setAlpha(_avatar, 255);
                _placed = true;
                _alpha = 255;
            }
        }

        public void Dispose() => Disarm();
    }
}
