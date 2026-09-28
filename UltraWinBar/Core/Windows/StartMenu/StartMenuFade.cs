using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    // Keeps a foreign Start menu invisible until it is placed, then fades it in.
    // DWMWA_CLOAK is denied across processes; a layered alpha is not, on windows that are not layered already.
    internal sealed class StartMenuFade : IDisposable
    {
        internal const int FadeMs = 150;
        // Never placed in time: shown where it is (or left alone if still hidden), as before hiding existed.
        internal const int MaxHiddenMs = 2000;
        private const int WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 2;
        private const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_FRAME = 0x400;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

        private sealed class Entry
        {
            public long HiddenAt;
            public long FadeStart = -1;
            public int FadeMs;
            public Action<byte> OnAlpha;
        }

        private readonly Dictionary<IntPtr, Entry> _windows = new Dictionary<IntPtr, Entry>();
        private DispatcherTimer _timer;

        internal int Count => _windows.Count;

        internal bool IsHidden(IntPtr hwnd) => _windows.TryGetValue(hwnd, out Entry entry) && entry.FadeStart < 0;

        // Ease-out, so most of the menu appears early.
        internal static byte AlphaAt(long elapsedMs, int fadeMs)
        {
            if (fadeMs <= 0 || elapsedMs >= fadeMs) return 255;
            if (elapsedMs <= 0) return 0;
            double t = 1 - (double)elapsedMs / fadeMs;
            return (byte)Math.Round(255 * (1 - t * t));
        }

        // onAlpha: told every alpha of the fade in, for companion windows (Open Shell's avatar).
        internal bool Hide(IntPtr hwnd, long now, Action<byte> onAlpha = null)
        {
            if (hwnd == IntPtr.Zero || _windows.ContainsKey(hwnd) || !IsWindow(hwnd)) return false;
            int exStyle = GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
            // Already layered: its owner drives the alpha (possibly per-pixel), so leave it alone.
            if ((exStyle & WS_EX_LAYERED) != 0) return false;
            SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            if (!SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA))
            {
                SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, exStyle);
                return false;
            }
            _windows[hwnd] = new Entry { HiddenAt = now, OnAlpha = onAlpha };
            EnsureTimer();
            return true;
        }

        // Call once the window is visible at its final place; fadeMs 0 shows it at once.
        internal void Reveal(IntPtr hwnd, long now, int fadeMs)
        {
            if (!_windows.TryGetValue(hwnd, out Entry entry) || entry.FadeStart >= 0) return;
            entry.FadeStart = now;
            entry.FadeMs = fadeMs;
            Tick(now);
        }

        // Closed or abandoned: opaque and unlayered again at once.
        internal void Restore(IntPtr hwnd)
        {
            if (!_windows.Remove(hwnd)) return;
            Unlayer(hwnd);
            StopTimerIfIdle();
        }

        internal void Tick(long now)
        {
            if (_windows.Count == 0) return;
            foreach (IntPtr hwnd in new List<IntPtr>(_windows.Keys))
            {
                Entry entry = _windows[hwnd];
                if (!IsWindow(hwnd))
                {
                    _windows.Remove(hwnd);
                    continue;
                }
                if (entry.FadeStart < 0)
                {
                    if (now - entry.HiddenAt < MaxHiddenMs) continue;
                    if (!IsWindowVisible(hwnd))
                    {
                        Restore(hwnd);
                        continue;
                    }
                    entry.FadeStart = now;
                    entry.FadeMs = FadeMs;
                }
                byte alpha = AlphaAt(now - entry.FadeStart, entry.FadeMs);
                if (alpha == 255) Restore(hwnd);
                else SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
                entry.OnAlpha?.Invoke(alpha);
            }
            StopTimerIfIdle();
        }

        private static void Unlayer(IntPtr hwnd)
        {
            if (!IsWindow(hwnd)) return;
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            int exStyle = GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
            SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, exStyle & ~WS_EX_LAYERED);
            // Per SetLayeredWindowAttributes docs: repaint after dropping the layered bit.
            RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RDW_ERASE | RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN);
        }

        private void EnsureTimer()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
                _timer.Tick += (s, e) => Tick(Environment.TickCount64);
            }
            if (!_timer.IsEnabled) _timer.Start();
        }

        private void StopTimerIfIdle()
        {
            if (_windows.Count == 0) _timer?.Stop();
        }

        public void Dispose()
        {
            foreach (IntPtr hwnd in new List<IntPtr>(_windows.Keys)) Restore(hwnd);
            _timer?.Stop();
        }
    }
}
