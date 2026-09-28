using System;
using System.Runtime.InteropServices;

namespace ManagedShell.Interop
{
    // Trust boundary (К16) for data handed to us by another process: WM_COPYDATA payloads (tray,
    // AppBar), AppBar shared memory, and the shell hook's SHELLHOOKINFO. Each Try* method is pure
    // given the bytes at the supplied address, so it can be exercised with garbage input in tests
    // without going through a real WndProc.
    internal static class CrossProcessMessages
    {
        // COPYDATASTRUCT.lpData/cbData describe a foreign buffer; never trust it larger than declared.
        internal static bool HasPayload(NativeMethods.COPYDATASTRUCT copyData, Type payload) =>
            copyData.lpData != IntPtr.Zero && copyData.cbData >= Marshal.SizeOf(payload);

        internal static bool TryReadNotifyIconMessage(NativeMethods.COPYDATASTRUCT copyData, out NativeMethods.SHELLTRAYDATA data)
        {
            data = default;
            if (!HasPayload(copyData, typeof(NativeMethods.SHELLTRAYDATA)))
            {
                return false;
            }

            data = Marshal.PtrToStructure<NativeMethods.SHELLTRAYDATA>(copyData.lpData);
            return true;
        }

        internal static bool TryReadIconIdentifier(NativeMethods.COPYDATASTRUCT copyData, out NativeMethods.WINNOTIFYICONIDENTIFIER data)
        {
            data = default;
            if (!HasPayload(copyData, typeof(NativeMethods.WINNOTIFYICONIDENTIFIER)))
            {
                return false;
            }

            data = Marshal.PtrToStructure<NativeMethods.WINNOTIFYICONIDENTIFIER>(copyData.lpData);
            return true;
        }

        // AppBar message data is a fixed-size struct embedded directly in the COPYDATASTRUCT
        // payload (unlike ABM_GETTASKBARPOS/QUERYPOS/SETPOS, whose real data travels via shared
        // memory - see TryReadSharedAppBarData). Both the outer and the embedded size must match.
        internal static bool TryReadAppBarMessage(NativeMethods.COPYDATASTRUCT copyData, out NativeMethods.APPBARMSGDATAV3 data)
        {
            data = default;
            if (copyData.lpData == IntPtr.Zero || Marshal.SizeOf<NativeMethods.APPBARMSGDATAV3>() != copyData.cbData)
            {
                return false;
            }

            data = Marshal.PtrToStructure<NativeMethods.APPBARMSGDATAV3>(copyData.lpData);
            return Marshal.SizeOf<NativeMethods.APPBARDATAV2>() == data.abd.cbSize;
        }

        // SHELLHOOK is a registered message any process in the session can post; lParam is not
        // marshaled by the system, so validate the pointer before reading or writing through it
        // (GETMINRECT overwrites the struct in place, hence read+write, not just read).
        internal static bool TryReadShellHookInfo(IntPtr lParam, out NativeMethods.SHELLHOOKINFO info)
        {
            info = default;
            if (!MemorySafety.IsReadWritable(lParam, Marshal.SizeOf<NativeMethods.SHELLHOOKINFO>()))
            {
                return false;
            }

            info = Marshal.PtrToStructure<NativeMethods.SHELLHOOKINFO>(lParam);
            return true;
        }

        // AppBar shared memory (SHLockShared) has no length the caller can query from the handle
        // alone; a peer can hand us a handle to a region smaller than APPBARDATAV2. Validate
        // before touching it, the same way GETMINRECT validates lParam above.
        internal static bool TryReadSharedAppBarData(IntPtr hShared, out NativeMethods.APPBARDATAV2 data)
        {
            data = default;
            if (!MemorySafety.IsReadWritable(hShared, Marshal.SizeOf<NativeMethods.APPBARDATAV2>()))
            {
                return false;
            }

            data = Marshal.PtrToStructure<NativeMethods.APPBARDATAV2>(hShared);
            return true;
        }

        internal static bool TryWriteSharedAppBarData(IntPtr hShared, NativeMethods.APPBARDATAV2 data)
        {
            if (!MemorySafety.IsReadWritable(hShared, Marshal.SizeOf<NativeMethods.APPBARDATAV2>()))
            {
                return false;
            }

            Marshal.StructureToPtr(data, hShared, false);
            return true;
        }
    }
}
