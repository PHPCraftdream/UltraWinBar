using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UltraWinBar.Utilities;

// StartMenuFade hides the Open Shell menu until it is placed, then fades it in. Real HWNDs from
// NativeWindow, the visible one 10x10 far off-screen; time is injected, so no sleeps.
internal static class StartMenuFadeChecks
{
    private const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    private const int WS_POPUP = unchecked((int)0x80000000), WS_VISIBLE = 0x10000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);

    internal static void Run()
    {
        RunAlphaCurveCheck();
        RunHideRevealCheck();
        RunSkipsLayeredWindowCheck();
        RunTimeoutCheck();
        Console.WriteLine("PASS: StartMenuFade hides a non-layered menu with alpha 0, fades it in once placed, restores its style on close/timeout/dispose, and never touches an already-layered window.");
        RunAvatarGuardCheck();
        Console.WriteLine("PASS: StartMenuAvatarGuard keeps the avatar transparent on its hook thread until placed (skipping the echo of its own alpha change), follows the menu fade, and makes it opaque on disarm or at full menu alpha.");
    }

    // Menu and avatar belong to this thread, as both belong to Open Shell's menu thread; a real per-pixel layered
    // avatar, so each alpha change raises the LOCATIONCHANGE echo the guard must not answer.
    private static void RunAvatarGuardCheck()
    {
        var menu = Create(visible: false, exStyle: 0);
        var avatar = Create(visible: false, exStyle: WS_EX_LAYERED);
        InitPerPixelLayered(avatar.Handle);
        var alphas = new System.Collections.Generic.List<byte>();
        int Count() { lock (alphas) return alphas.Count; }
        byte Last() { lock (alphas) return alphas[alphas.Count - 1]; }
        var guard = new StartMenuAvatarGuard(h => h == avatar.Handle, (h, a) =>
        {
            StartMenuAvatarGuard.SetAlpha(h, a);
            lock (alphas) alphas.Add(a);
        });
        int hooksBefore = ManagedShell.Common.Native.WinEventHook.InstalledCount;
        try
        {
            guard.Arm(menu.Handle);
            if (!guard.IsArmed) throw new Exception("The avatar guard did not arm its hook thread.");
            SetWindowPos(avatar.Handle, IntPtr.Zero, -10000, -9990, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            if (!PumpUntil(() => Count() > 0) || Last() != 0) throw new Exception("A moved, unplaced avatar must be made transparent.");
            PumpUntil(() => false, 200);
            if (Count() > 3) throw new Exception($"The guard answered the echo of its own alpha change: {Count()} changes for one move.");

            int beforeShow = Count();
            ShowWindow(avatar.Handle, SW_SHOWNOACTIVATE);
            if (!PumpUntil(() => Count() > beforeShow) || Last() != 0) throw new Exception("A shown, unplaced avatar must be made transparent.");

            PumpUntil(() => false, 100);
            int beforeFade = Count();
            guard.Show(100);
            if (Count() != beforeFade) throw new Exception("The menu fade must not show an avatar that is not placed yet.");
            guard.MarkPlaced(avatar.Handle);
            if (Count() != beforeFade + 1 || Last() != 100) throw new Exception("A placed avatar must take the menu's current fade alpha.");
            SetWindowPos(avatar.Handle, IntPtr.Zero, -10000, -9980, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            PumpUntil(() => false, 100);
            if (Last() != 100) throw new Exception("A placed avatar must not be hidden again.");
            guard.Show(255);
            if (Last() != 255 || guard.IsArmed) throw new Exception("At full menu alpha the avatar must be opaque and the guard done.");

            guard.Arm(menu.Handle);
            SetWindowPos(avatar.Handle, IntPtr.Zero, -10000, -9970, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            if (!PumpUntil(() => Last() == 0)) throw new Exception("A re-armed guard must hide the avatar again.");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            guard.Disarm();
            if (clock.ElapsedMilliseconds > 500) throw new Exception($"Disarm must not wait for the hook thread, took {clock.ElapsedMilliseconds} ms.");
            if (Last() != 255) throw new Exception("Disarming (menu closed) must make a hidden avatar opaque.");
            if (!PumpUntil(() => ManagedShell.Common.Native.WinEventHook.InstalledCount == hooksBefore))
                throw new Exception("The disarmed hook thread must unhook and end.");

            // Open Shell moves and shows the avatar (resetting its alpha) before our echo arrives: an event for a
            // changed window is never taken for the echo. A recording-only setter raises no echo, so one stays pending.
            int hides = 0;
            var echoless = new StartMenuAvatarGuard(h => h == avatar.Handle, (h, a) => { if (a == 0) System.Threading.Interlocked.Increment(ref hides); });
            try
            {
                echoless.Arm(menu.Handle);
                SetWindowPos(avatar.Handle, IntPtr.Zero, -10000, -9960, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                if (!PumpUntil(() => hides == 1)) throw new Exception("The avatar was not hidden after a move.");
                SetWindowPos(avatar.Handle, IntPtr.Zero, -10000, -9950, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                if (!PumpUntil(() => hides == 2)) throw new Exception("A move while an echo was pending was taken for the echo and not answered.");
            }
            finally
            {
                echoless.Dispose();
            }
        }
        finally
        {
            guard.Dispose();
            avatar.DestroyHandle();
            menu.DestroyHandle();
        }
    }

    // Gives the window a 16x16 per-pixel alpha surface, like Open Shell's avatar.
    private static void InitPerPixelLayered(IntPtr hwnd)
    {
        IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen);
        var info = new BitmapInfoHeader { Size = 40, Width = 16, Height = 16, Planes = 1, BitCount = 32 };
        IntPtr bitmap = CreateDIBSection(memory, ref info, 0, out _, IntPtr.Zero, 0);
        IntPtr old = SelectObject(memory, bitmap);
        var size = new SizeStruct { Cx = 16, Cy = 16 };
        var source = new PointStruct();
        var blend = new Blend { SourceConstantAlpha = 255, AlphaFormat = 1 };
        bool ok = UpdateLayeredWindow(hwnd, screen, IntPtr.Zero, ref size, memory, ref source, 0, ref blend, 2);
        SelectObject(memory, old);
        DeleteObject(bitmap);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
        if (!ok) throw new Exception("Setup failed: could not give the test avatar a per-pixel layered surface.");
    }

    private const uint SWP_NOSIZE = 0x1;
    private const int SW_SHOWNOACTIVATE = 4;
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct SizeStruct { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)] private struct PointStruct { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Blend { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SizeStruct size, IntPtr hdcSrc, ref PointStruct pptSrc, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    private static bool PumpUntil(Func<bool> done, int timeoutMs = 2000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            Application.DoEvents();
            if (done()) return true;
            System.Threading.Thread.Sleep(5);
        }
        return done();
    }


    private const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static void RunAlphaCurveCheck()
    {
        byte previous = 0;
        for (int ms = 0; ms <= StartMenuFade.FadeMs; ms += 5)
        {
            byte alpha = StartMenuFade.AlphaAt(ms, StartMenuFade.FadeMs);
            if (alpha < previous) throw new Exception($"Fade alpha must not decrease: {previous} -> {alpha} at {ms} ms.");
            previous = alpha;
        }
        if (StartMenuFade.AlphaAt(0, StartMenuFade.FadeMs) != 0 || previous != 255 || StartMenuFade.AlphaAt(0, 0) != 255)
            throw new Exception("Fade must start transparent, end opaque, and be opaque at once without animation.");
    }

    private static void RunHideRevealCheck()
    {
        var window = Create(visible: true, exStyle: 0);
        IntPtr hwnd = window.Handle;
        var fade = new StartMenuFade();
        try
        {
            if (!fade.Hide(hwnd, 1000) || !fade.IsHidden(hwnd) || !IsLayered(hwnd) || Alpha(hwnd) != 0)
                throw new Exception("Hide must make a non-layered window layered with alpha 0.");
            if (fade.Hide(hwnd, 1001)) throw new Exception("Hiding an already hidden window must be a no-op.");

            fade.Reveal(hwnd, 1100, StartMenuFade.FadeMs);
            fade.Tick(1100 + StartMenuFade.FadeMs / 2);
            byte mid = Alpha(hwnd);
            if (fade.IsHidden(hwnd) || mid == 0 || mid == 255)
                throw new Exception($"Halfway through the fade the menu must be partly visible, got alpha {mid}.");
            fade.Tick(1100 + StartMenuFade.FadeMs);
            if (fade.Count != 0 || IsLayered(hwnd))
                throw new Exception("A finished fade must drop the layered style it added.");

            fade.Hide(hwnd, 2000);
            fade.Reveal(hwnd, 2050, 0);
            if (fade.Count != 0 || IsLayered(hwnd)) throw new Exception("Reveal without animation must show the menu at once.");

            fade.Hide(hwnd, 3000);
            fade.Restore(hwnd);
            if (fade.Count != 0 || IsLayered(hwnd)) throw new Exception("Restore (menu closed) must drop the layered style at once.");

            fade.Hide(hwnd, 4000);
            fade.Dispose();
            if (fade.Count != 0 || IsLayered(hwnd)) throw new Exception("Dispose must restore every hidden menu.");

            var destroyed = Create(visible: false, exStyle: 0);
            fade.Hide(destroyed.Handle, 5000);
            destroyed.DestroyHandle();
            fade.Tick(5001);
            if (fade.Count != 0) throw new Exception("A destroyed menu must be forgotten.");
        }
        finally
        {
            fade.Dispose();
            window.DestroyHandle();
        }
    }

    private static void RunSkipsLayeredWindowCheck()
    {
        var window = Create(visible: false, exStyle: WS_EX_LAYERED);
        var fade = new StartMenuFade();
        try
        {
            int before = GetWindowLong(window.Handle, GWL_EXSTYLE);
            if (fade.Hide(window.Handle, 0) || fade.Count != 0 || GetWindowLong(window.Handle, GWL_EXSTYLE) != before)
                throw new Exception("An already-layered window (its owner drives the alpha) must be left alone.");
        }
        finally
        {
            fade.Dispose();
            window.DestroyHandle();
        }
    }

    // Never placed: a visible menu fades in where it is, a still-hidden one is just restored.
    private static void RunTimeoutCheck()
    {
        var visible = Create(visible: true, exStyle: 0);
        var hidden = Create(visible: false, exStyle: 0);
        var fade = new StartMenuFade();
        try
        {
            fade.Hide(visible.Handle, 0);
            fade.Hide(hidden.Handle, 0);
            fade.Tick(StartMenuFade.MaxHiddenMs - 1);
            if (!fade.IsHidden(visible.Handle) || !fade.IsHidden(hidden.Handle))
                throw new Exception("Before the timeout both menus must stay hidden.");
            fade.Tick(StartMenuFade.MaxHiddenMs);
            if (fade.IsHidden(visible.Handle) || fade.Count != 1 || IsLayered(hidden.Handle))
                throw new Exception("At the timeout a visible menu must start fading in and a hidden one must be restored.");
            fade.Tick(StartMenuFade.MaxHiddenMs + StartMenuFade.FadeMs);
            if (fade.Count != 0 || IsLayered(visible.Handle))
                throw new Exception("The timeout fade must finish and restore the style.");
        }
        finally
        {
            fade.Dispose();
            visible.DestroyHandle();
            hidden.DestroyHandle();
        }
    }

    private static NativeWindow Create(bool visible, int exStyle)
    {
        var window = new NativeWindow();
        window.CreateHandle(new CreateParams
        {
            X = -10000, Y = -10000, Width = 10, Height = 10,
            Style = WS_POPUP | (visible ? WS_VISIBLE : 0),
            ExStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | exStyle,
        });
        return window;
    }

    private static bool IsLayered(IntPtr hwnd) => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0;

    private static byte Alpha(IntPtr hwnd) => GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out _) ? alpha : (byte)255;
}
