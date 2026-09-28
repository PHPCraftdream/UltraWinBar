using System;
using System.Runtime.InteropServices;

namespace ManagedShell.Interop
{
    // Trust-boundary helper (К16/Н5): validates a foreign pointer with VirtualQuery before it is
    // dereferenced. Pure and static so it can be exercised with garbage input in tests. Shared by
    // every assembly that parses data handed to us by another process (tray, AppBar, shell hook).
    internal static class MemorySafety
    {
        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_GUARD = 0x100;
        private const uint PAGE_READABLE = 0x02 /*READONLY*/ | 0x04 /*READWRITE*/ | 0x08 /*WRITECOPY*/
                                          | 0x20 /*EXECUTE_READ*/ | 0x40 /*EXECUTE_READWRITE*/ | 0x80 /*EXECUTE_WRITECOPY*/;
        private const uint PAGE_WRITABLE = 0x04 /*READWRITE*/ | 0x08 /*WRITECOPY*/ | 0x40 /*EXECUTE_READWRITE*/ | 0x80 /*EXECUTE_WRITECOPY*/;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

        private static bool TryQuery(IntPtr address, out MEMORY_BASIC_INFORMATION mbi)
        {
            mbi = default;
            if (address == IntPtr.Zero)
            {
                return false;
            }

            UIntPtr size = (UIntPtr)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
            return VirtualQuery(address, out mbi, size) != UIntPtr.Zero;
        }

        // True only if [address, address+size) lies entirely inside one committed region that is
        // both readable and writable (not PAGE_GUARD / PAGE_NOACCESS).
        internal static bool IsReadWritable(IntPtr address, int size)
        {
            if (address == IntPtr.Zero || size <= 0 || !TryQuery(address, out var mbi))
            {
                return false;
            }

            if (mbi.State != MEM_COMMIT) return false;
            if ((mbi.Protect & PAGE_GUARD) != 0) return false;
            if ((mbi.Protect & PAGE_WRITABLE) == 0) return false;

            return FitsInRegion(address, size, mbi);
        }

        // Bytes readable from address to the end of its committed, readable region, capped at
        // maxBytes; 0 if address is not safely readable at all.
        internal static int GetReadableByteCount(IntPtr address, int maxBytes)
        {
            if (address == IntPtr.Zero || maxBytes <= 0 || !TryQuery(address, out var mbi))
            {
                return 0;
            }

            if (mbi.State != MEM_COMMIT) return 0;
            if ((mbi.Protect & PAGE_GUARD) != 0) return 0;
            if ((mbi.Protect & PAGE_READABLE) == 0) return 0;

            long regionEnd = Addr(mbi.BaseAddress) + (long)mbi.RegionSize;
            long available = regionEnd - Addr(address);
            if (available <= 0) return 0;

            return (int)Math.Min(available, maxBytes);
        }

        // Unsigned: ToInt64 sign-extends 32-bit addresses above 2 GB.
        private static long Addr(IntPtr p) => unchecked((long)(ulong)(nuint)(nint)p);

        private static bool FitsInRegion(IntPtr address, int size, MEMORY_BASIC_INFORMATION mbi)
        {
            long regionStart = Addr(mbi.BaseAddress);
            long regionEnd = regionStart + (long)mbi.RegionSize;
            long rangeStart = Addr(address);
            long rangeEnd = rangeStart + size;
            return rangeStart >= regionStart && rangeEnd <= regionEnd;
        }
    }
}
