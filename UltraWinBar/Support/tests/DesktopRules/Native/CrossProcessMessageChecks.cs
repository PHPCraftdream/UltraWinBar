using System;
using System.Runtime.InteropServices;
using ManagedShell.Interop;

// R9-M / К16/К20: garbage-input coverage for the trust-boundary parsers in
// ManagedShell.Interop.CrossProcessMessages - the pure functions TrayService, AppBarManager and
// TasksService now call instead of marshalling foreign data inline.
internal static class CrossProcessMessageChecks
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_READONLY = 0x02;
    private const uint PAGE_NOACCESS = 0x01;

    internal static void Run()
    {
        CheckNotifyIconMessage();
        CheckIconIdentifier();
        CheckAppBarMessage();
        CheckShellHookInfo();
        CheckSharedAppBarData();
    }

    private static void CheckNotifyIconMessage()
    {
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.SHELLTRAYDATA>());
        try
        {
            Marshal.StructureToPtr(new NativeMethods.SHELLTRAYDATA { dwMessage = 42 }, buffer, false);
            var valid = new NativeMethods.COPYDATASTRUCT { lpData = buffer, cbData = Marshal.SizeOf<NativeMethods.SHELLTRAYDATA>() };
            if (!CrossProcessMessages.TryReadNotifyIconMessage(valid, out var data) || data.dwMessage != 42)
                throw new Exception("A correctly sized notify icon payload must be read.");

            var shortPayload = new NativeMethods.COPYDATASTRUCT { lpData = buffer, cbData = Marshal.SizeOf<NativeMethods.SHELLTRAYDATA>() - 1 };
            if (CrossProcessMessages.TryReadNotifyIconMessage(shortPayload, out _))
                throw new Exception("A notify icon payload shorter than SHELLTRAYDATA must be rejected.");

            var nullPayload = new NativeMethods.COPYDATASTRUCT { lpData = IntPtr.Zero, cbData = Marshal.SizeOf<NativeMethods.SHELLTRAYDATA>() };
            if (CrossProcessMessages.TryReadNotifyIconMessage(nullPayload, out _))
                throw new Exception("A null notify icon payload must be rejected.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        Console.WriteLine("PASS: CrossProcessMessages.TryReadNotifyIconMessage rejects short/null WM_COPYDATA payloads and reads a correctly sized one.");
    }

    private static void CheckIconIdentifier()
    {
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINNOTIFYICONIDENTIFIER>());
        try
        {
            Marshal.StructureToPtr(new NativeMethods.WINNOTIFYICONIDENTIFIER { dwMessage = 7, uID = 3 }, buffer, false);
            var valid = new NativeMethods.COPYDATASTRUCT { lpData = buffer, cbData = Marshal.SizeOf<NativeMethods.WINNOTIFYICONIDENTIFIER>() };
            if (!CrossProcessMessages.TryReadIconIdentifier(valid, out var data) || data.dwMessage != 7 || data.uID != 3)
                throw new Exception("A correctly sized icon identifier payload must be read.");

            var shortPayload = new NativeMethods.COPYDATASTRUCT { lpData = buffer, cbData = Marshal.SizeOf<NativeMethods.WINNOTIFYICONIDENTIFIER>() - 1 };
            if (CrossProcessMessages.TryReadIconIdentifier(shortPayload, out _))
                throw new Exception("An icon identifier payload shorter than WINNOTIFYICONIDENTIFIER must be rejected.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        Console.WriteLine("PASS: CrossProcessMessages.TryReadIconIdentifier rejects a short WM_COPYDATA payload and reads a correctly sized one.");
    }

    private static void CheckAppBarMessage()
    {
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.APPBARMSGDATAV3>());
        try
        {
            var goodAmd = new NativeMethods.APPBARMSGDATAV3
            {
                abd = new NativeMethods.APPBARDATAV2 { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATAV2>() },
                dwMessage = 5
            };
            Marshal.StructureToPtr(goodAmd, buffer, false);
            var valid = new NativeMethods.COPYDATASTRUCT { lpData = buffer, cbData = Marshal.SizeOf<NativeMethods.APPBARMSGDATAV3>() };
            if (!CrossProcessMessages.TryReadAppBarMessage(valid, out var data) || data.dwMessage != 5)
                throw new Exception("A correctly sized AppBar message with a matching embedded cbSize must be read.");

            // Wrong outer cbData: the COPYDATASTRUCT payload does not match APPBARMSGDATAV3 at all.
            var wrongCbData = new NativeMethods.COPYDATASTRUCT { lpData = buffer, cbData = Marshal.SizeOf<NativeMethods.APPBARMSGDATAV3>() - 1 };
            if (CrossProcessMessages.TryReadAppBarMessage(wrongCbData, out _))
                throw new Exception("An AppBar message with the wrong outer cbData must be rejected.");

            // Right outer size, but the embedded AppBarData claims a different (mismatched) size.
            var mismatched = goodAmd;
            mismatched.abd.cbSize = 0;
            Marshal.StructureToPtr(mismatched, buffer, false);
            var mismatchedPayload = new NativeMethods.COPYDATASTRUCT { lpData = buffer, cbData = Marshal.SizeOf<NativeMethods.APPBARMSGDATAV3>() };
            if (CrossProcessMessages.TryReadAppBarMessage(mismatchedPayload, out _))
                throw new Exception("An AppBar message whose embedded AppBarData.cbSize does not match APPBARDATAV2 must be rejected.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        Console.WriteLine("PASS: CrossProcessMessages.TryReadAppBarMessage rejects a wrong outer cbData or a mismatched embedded AppBarData size.");
    }

    private static void CheckShellHookInfo()
    {
        if (CrossProcessMessages.TryReadShellHookInfo(IntPtr.Zero, out _))
            throw new Exception("A null SHELLHOOKINFO pointer must be rejected.");
        if (CrossProcessMessages.TryReadShellHookInfo((IntPtr)1, out _))
            throw new Exception("A garbage SHELLHOOKINFO pointer must be rejected.");

        UIntPtr pageSize = (UIntPtr)4096;
        IntPtr unmapped = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (unmapped == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc failed.");
        if (!VirtualFree(unmapped, UIntPtr.Zero, MEM_RELEASE)) throw new Exception("Test setup: VirtualFree failed.");
        if (CrossProcessMessages.TryReadShellHookInfo(unmapped, out _))
            throw new Exception("An unmapped SHELLHOOKINFO pointer must be rejected.");

        IntPtr noAccess = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (noAccess == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc failed.");
        try
        {
            if (!VirtualProtect(noAccess, pageSize, PAGE_NOACCESS, out _)) throw new Exception("Test setup: VirtualProtect(PAGE_NOACCESS) failed.");
            if (CrossProcessMessages.TryReadShellHookInfo(noAccess, out _))
                throw new Exception("A PAGE_NOACCESS SHELLHOOKINFO pointer must be rejected.");
        }
        finally
        {
            VirtualFree(noAccess, UIntPtr.Zero, MEM_RELEASE);
        }

        IntPtr readOnly = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READONLY);
        if (readOnly == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc(PAGE_READONLY) failed.");
        try
        {
            // GETMINRECT overwrites the struct in place, so a read-only page must be rejected too.
            if (CrossProcessMessages.TryReadShellHookInfo(readOnly, out _))
                throw new Exception("A PAGE_READONLY SHELLHOOKINFO pointer must be rejected (GETMINRECT writes back through it).");
        }
        finally
        {
            VirtualFree(readOnly, UIntPtr.Zero, MEM_RELEASE);
        }

        IntPtr rw = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (rw == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc(PAGE_READWRITE) failed.");
        try
        {
            Marshal.StructureToPtr(new NativeMethods.SHELLHOOKINFO { hwnd = (IntPtr)0x1234 }, rw, false);
            if (!CrossProcessMessages.TryReadShellHookInfo(rw, out var info) || info.hwnd != (IntPtr)0x1234)
                throw new Exception("A valid read-write SHELLHOOKINFO pointer must be read correctly.");
        }
        finally
        {
            VirtualFree(rw, UIntPtr.Zero, MEM_RELEASE);
        }

        Console.WriteLine("PASS: CrossProcessMessages.TryReadShellHookInfo rejects null/garbage/unmapped/no-access/read-only pointers and reads a valid read-write one.");
    }

    private static void CheckSharedAppBarData()
    {
        if (CrossProcessMessages.TryReadSharedAppBarData(IntPtr.Zero, out _))
            throw new Exception("A null shared AppBarData pointer must be rejected.");
        if (CrossProcessMessages.TryWriteSharedAppBarData(IntPtr.Zero, default))
            throw new Exception("A null shared AppBarData pointer must be rejected for writes.");

        UIntPtr pageSize = (UIntPtr)4096;
        IntPtr readOnly = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READONLY);
        if (readOnly == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc(PAGE_READONLY) failed.");
        try
        {
            // ABM_GETTASKBARPOS both reads and writes the shared region, so read-only must be rejected too.
            if (CrossProcessMessages.TryReadSharedAppBarData(readOnly, out _))
                throw new Exception("A PAGE_READONLY shared AppBarData pointer must be rejected.");
            if (CrossProcessMessages.TryWriteSharedAppBarData(readOnly, default))
                throw new Exception("A PAGE_READONLY shared AppBarData pointer must be rejected for writes.");
        }
        finally
        {
            VirtualFree(readOnly, UIntPtr.Zero, MEM_RELEASE);
        }

        IntPtr rw = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (rw == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc(PAGE_READWRITE) failed.");
        try
        {
            var abd = new NativeMethods.APPBARDATAV2 { uEdge = 2 };
            if (!CrossProcessMessages.TryWriteSharedAppBarData(rw, abd))
                throw new Exception("A valid read-write shared AppBarData pointer must accept a write.");
            if (!CrossProcessMessages.TryReadSharedAppBarData(rw, out var readBack) || readBack.uEdge != 2)
                throw new Exception("A write followed by a read on a valid pointer must round-trip.");
        }
        finally
        {
            VirtualFree(rw, UIntPtr.Zero, MEM_RELEASE);
        }

        Console.WriteLine("PASS: CrossProcessMessages.TryReadSharedAppBarData/TryWriteSharedAppBarData reject null/read-only shared memory and round-trip on a valid region.");
    }
}
