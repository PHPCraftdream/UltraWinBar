using ManagedShell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    // Who is behind a window, for the window journal: its process and the processes that started it,
    // as `name(pid)[command line] <- parent(pid)[command line] <- ...`. `?(pid)` marks a process that is gone
    // (a short-lived console window) or cannot be read.
    internal static class ProcessOrigin
    {
        internal sealed class Info
        {
            public string Name;
            public string CommandLine;
            public uint ParentId;
            public long StartedAt;
        }

        private const int MaxLevels = 4;
        private const int MaxCommandLength = 160;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr hProcess, int informationClass,
            ref ProcessBasicInformation information, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr hProcess, int informationClass,
            IntPtr information, int length, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr hProcess, out long creationTime, out long exitTime,
            out long kernelTime, out long userTime);

        // The chain for the process that owns a window.
        internal static string OfWindow(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            return Describe(processId, Read);
        }

        // lookup returns null for a process that is gone or cannot be opened. A parent that started after its child
        // is a reused id of a process that is long gone, so the chain ends there instead of naming a stranger.
        internal static string Describe(uint processId, Func<uint, Info> lookup, int levels = MaxLevels)
        {
            var parts = new List<string>();
            var seen = new HashSet<uint>();
            Info child = null;
            for (int level = 0; level < levels && processId != 0 && seen.Add(processId); level++)
            {
                Info info = lookup(processId);
                if (info == null || (child != null && info.StartedAt > child.StartedAt))
                {
                    parts.Add($"?({processId})");
                    break;
                }
                parts.Add(Format(info, processId));
                processId = info.ParentId;
                child = info;
            }
            return string.Join(" <- ", parts);
        }

        private static string Format(Info info, uint processId)
        {
            string command = Clean(info.CommandLine);
            if (command.Length > MaxCommandLength) command = command.Substring(0, MaxCommandLength) + "...";
            string name = string.IsNullOrEmpty(info.Name) ? "?" : Clean(info.Name);
            return command.Length == 0 ? $"{name}({processId})" : $"{name}({processId})[{command}]";
        }

        private static string Clean(string value) =>
            string.IsNullOrEmpty(value) ? "" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

        internal static Info Read(uint processId)
        {
            IntPtr process = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, (int)processId);
            if (process == IntPtr.Zero) return null;
            try
            {
                var info = new Info { Name = "?" };
                var path = new StringBuilder(1024);
                int size = path.Capacity;
                if (QueryFullProcessImageName(process, 0, path, ref size)) info.Name = Path.GetFileName(path.ToString(0, size));

                var basic = new ProcessBasicInformation();
                if (NtQueryInformationProcess(process, 0, ref basic, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0)
                    info.ParentId = (uint)basic.InheritedFromUniqueProcessId.ToInt64();
                if (GetProcessTimes(process, out long created, out _, out _, out _)) info.StartedAt = created;
                info.CommandLine = ReadCommandLine(process);
                return info;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        // ProcessCommandLineInformation (60): a UNICODE_STRING whose buffer follows it in the same allocation.
        private static string ReadCommandLine(IntPtr process)
        {
            int capacity = 4096;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(capacity);
                try
                {
                    int status = NtQueryInformationProcess(process, 60, buffer, capacity, out int needed);
                    if (status == 0)
                    {
                        int length = (ushort)Marshal.ReadInt16(buffer, 0);
                        IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                        return length == 0 || text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(text, length / 2);
                    }
                    // STATUS_INFO_LENGTH_MISMATCH / STATUS_BUFFER_OVERFLOW / STATUS_BUFFER_TOO_SMALL: retry once, bounded.
                    if (needed <= capacity || needed > 65536) return "";
                    capacity = needed;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return "";
        }
    }
}
