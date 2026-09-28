using ManagedShell.Common.Logging;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.IO;
using System;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    static class TrayHotkey
    {
        public struct Entry
        {
            public int Id;
            public byte VirtualKey;
            public byte Modifier;
        }

        /// <summary>
        /// Extracts the Windows hotkey table registered by Shell_TrayWnd by reading the binary file of the tray process.
        /// </summary>
        /// <param name="trayHandle">Handle to the tray process</param>
        /// <returns>A list of hotkey entries found in the tray process</returns>
        /// <remarks>
        /// This method works by scanning the .exe for byte patterns that match the structure of hotkey entries.
        /// It identifies sequences of bytes that follow the pattern where:
        /// - Each entry is 8 bytes
        /// - Only byte 0 (virtual key code) and byte 4 (modifier) have non-zero values
        /// - The modifier has the Windows key flag set
        ///
        /// The method finds the longest consecutive sequence of valid entries, which is likely
        /// the hotkey table used by Shell_TrayWnd to register system-wide Windows+Key combinations.
        /// </remarks>
        public static List<Entry> BuildTable(IntPtr trayHandle)
        {
            string shellTrayProcessPath = GetShellTrayProcessPath(trayHandle);
            if (string.IsNullOrEmpty(shellTrayProcessPath))
            {
                throw new FileNotFoundException("Failed to get process path for Shell_TrayWnd");
            }

            using var mmf = MemoryMappedFile.CreateFromFile(shellTrayProcessPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            return ScanForLongestRun(stream);
        }

        private const int ENTRY_SIZE = 8;

        /// <summary>
        /// Single forward pass over the stream in ENTRY_SIZE-aligned windows: O(N) time, and the
        /// only bytes retained are those of the current and best candidate runs (not the whole
        /// file). Replaces a full-file copy plus an O(K^2) List.Contains scan over candidate
        /// offsets.
        /// </summary>
        internal static List<Entry> ScanForLongestRun(Stream stream)
        {
            const int WINDOW_ENTRIES = 8192; // 64 KiB read buffer, aligned to ENTRY_SIZE
            byte[] buffer = new byte[ENTRY_SIZE * WINDOW_ENTRIES];

            List<Entry> currentRun = new List<Entry>();
            List<Entry> bestRun = new List<Entry>();

            int read;
            while ((read = ReadWindow(stream, buffer)) > 0)
            {
                for (int i = 0; i + ENTRY_SIZE <= read; i += ENTRY_SIZE)
                {
                    if (IsValidEntryAt(buffer, i))
                    {
                        currentRun.Add(new Entry
                        {
                            VirtualKey = buffer[i],
                            Modifier = buffer[i + 4]
                        });
                    }
                    else if (currentRun.Count > 0)
                    {
                        if (currentRun.Count > bestRun.Count)
                        {
                            (bestRun, currentRun) = (currentRun, bestRun);
                        }

                        currentRun.Clear();
                    }
                }
            }

            if (currentRun.Count > bestRun.Count)
            {
                bestRun = currentRun;
            }

            // Shell_TrayWnd hotkey IDs start at 500, contiguous regardless of where the run was found.
            for (int i = 0; i < bestRun.Count; i++)
            {
                Entry e = bestRun[i];
                e.Id = i + 500;
                bestRun[i] = e;
            }

            return bestRun;
        }

        // Fills buffer as much as the stream allows; may return less than buffer.Length only at EOF.
        private static int ReadWindow(Stream stream, byte[] buffer)
        {
            int total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
            }

            return total;
        }

        /// <summary>
        /// Checks if the bytes at the specified index match the pattern of a Windows hotkey entry in the tray process.
        /// </summary>
        /// <param name="bytes">The byte array to check</param>
        /// <param name="offset">The offset to start checking at</param>
        /// <returns>True if the bytes at offset match the hotkey entry pattern</returns>
        private static bool IsValidEntryAt(byte[] bytes, int offset)
        {
            if (offset % ENTRY_SIZE != 0)
                return false;

            // Only byte 0 (VK code) and byte 4 (modifier) should have non-zero values; the rest must be zero.
            for (int i = 1; i < ENTRY_SIZE; i++)
            {
                if (i != 4 && bytes[offset + i] != 0)
                    return false;
            }

            // Validate modifier: must have the Windows key flag set and high bits should be clear.
            byte modifier = bytes[offset + 4];
            return (modifier & (byte)MOD.WIN) != 0 && (modifier & 0xF0) == 0;
        }

        #region Process-related API calls
        private static readonly string SystemExplorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        public static string GetShellTrayProcessPath(IntPtr trayHandle)
        {
            IntPtr processHandle = IntPtr.Zero;
            try
            {
                GetWindowThreadProcessId(trayHandle, out uint processId);
                processHandle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, (int)processId);
                if (processHandle == IntPtr.Zero)
                {
                    ShellLogger.Warning($"TrayHotkey: OpenProcess failed (Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
                    return null;
                }

                string executablePath = GetExecutablePath(processHandle);
                return !string.IsNullOrEmpty(executablePath) ? executablePath : SystemExplorerPath;
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"TrayHotkey: Failed to get process path - {ex.Message}");
                return null;
            }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    CloseHandle(processHandle);
                }
            }
        }

        private static string GetExecutablePath(IntPtr processHandle)
        {
            try
            {
                System.Text.StringBuilder stringBuilder = new(4096);
                int size = stringBuilder.Capacity;
                if (QueryFullProcessImageName(processHandle, 0, stringBuilder, ref size))
                {
                    return stringBuilder.ToString(0, size);
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"TrayHotkey: Error getting executable path - {ex.Message}");
            }
            return null;
        }
        #endregion
    }
}