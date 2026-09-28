using System;
using System.Collections.Generic;
using System.IO;
using ManagedShell.Interop;
using UltraWinBar.Utilities;

// R9-N: TrayHotkey.BuildTable used to copy the whole explorer.exe into memory and find the longest
// candidate run with List.Contains in a loop (O(K^2) in the number of candidate 8-byte entries).
// It now streams the file once (TrayHotkey.ScanForLongestRun), O(N), without a full-file copy.
// This keeps the old algorithm here as the oracle and compares it against the new one on synthetic
// byte buffers (no real explorer.exe needed).
internal static class TrayHotkeyChecks
{
    internal static void Run()
    {
        CheckAgainstOracle("empty buffer", new byte[0]);
        CheckAgainstOracle("no valid entries", new byte[256]);
        CheckAgainstOracle("single short run", BuildBuffer(4096, (100, 3)));
        CheckAgainstOracle("two runs, second longer", BuildBuffer(4096, (40, 2), (2000, 7)));
        CheckAgainstOracle("two runs, first longer (tie-break keeps the first)", BuildBuffer(4096, (40, 5), (2000, 5)));
        CheckAgainstOracle("trailing partial entry", BuildBuffer(103, (16, 4))); // 103 % 8 != 0
        CheckAgainstOracle("run adjacent to buffer end", BuildBuffer(80, (48, 4)));

        // Large enough to force TrayHotkey's internal read window (64 KiB) to split across two
        // reads, with the winning run straddling that boundary: proves the run state survives
        // across ReadWindow calls instead of resetting per chunk.
        const int windowBytes = 8 * 8192;
        byte[] straddling = BuildBuffer(windowBytes + 4096, (windowBytes - 40, 20));
        CheckAgainstOracle("run straddling the internal read-window boundary", straddling);

        Console.WriteLine("PASS: TrayHotkey.ScanForLongestRun matches the original O(K^2) full-copy algorithm on empty, entry-less, tied, boundary, and read-window-straddling buffers.");
    }

    private static void CheckAgainstOracle(string label, byte[] data)
    {
        List<TrayHotkey.Entry> expected = OldAlgorithm(data);
        List<TrayHotkey.Entry> actual;
        using (var stream = new MemoryStream(data, writable: false))
        {
            actual = TrayHotkey.ScanForLongestRun(stream);
        }

        if (expected.Count != actual.Count)
            throw new Exception($"TrayHotkey ({label}): entry count mismatch (oracle={expected.Count}, new={actual.Count}).");
        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i].Id != actual[i].Id || expected[i].VirtualKey != actual[i].VirtualKey || expected[i].Modifier != actual[i].Modifier)
                throw new Exception($"TrayHotkey ({label}): entry {i} mismatch (oracle Id={expected[i].Id} VK={expected[i].VirtualKey} Mod={expected[i].Modifier}, " +
                    $"new Id={actual[i].Id} VK={actual[i].VirtualKey} Mod={actual[i].Modifier}).");
        }
    }

    // Fills entry `start[i]` with a plausible Windows-key hotkey entry for i in [0, count).
    private static byte[] BuildBuffer(int size, params (int start, int count)[] runs)
    {
        byte[] data = new byte[size];
        foreach (var (start, count) in runs)
        {
            for (int i = 0; i < count; i++)
            {
                int off = start + i * 8;
                data[off] = (byte)('A' + (i % 26)); // arbitrary non-zero virtual key code
                data[off + 4] = (byte)NativeMethods.MOD.WIN; // Windows-key flag, high bits clear
            }
        }
        return data;
    }

    // The pre-R9-N algorithm: full-array scan for candidate offsets, then an O(K^2) longest-run
    // search via List.Contains. Kept verbatim (modulo types) as the test oracle.
    private static List<TrayHotkey.Entry> OldAlgorithm(byte[] data)
    {
        var offsets = new List<int>();
        for (int i = 0; i <= data.Length - 8; i += 8)
        {
            if (IsValidEntryAt(data, i))
                offsets.Add(i);
        }

        int bestStart = 0, bestCount = 0;
        foreach (int start in offsets)
        {
            int count = 1;
            while (offsets.Contains(start + count * 8)) count++;
            if (count > bestCount)
            {
                bestStart = start;
                bestCount = count;
            }
        }

        var table = new List<TrayHotkey.Entry>(bestCount);
        for (int i = 0; i < bestCount; i++)
        {
            int off = bestStart + i * 8;
            table.Add(new TrayHotkey.Entry
            {
                Id = i + 500,
                VirtualKey = data[off],
                Modifier = data[off + 4]
            });
        }

        return table;
    }

    private static bool IsValidEntryAt(byte[] bytes, int offset)
    {
        const int entrySize = 8;
        if (offset % entrySize != 0)
            return false;

        for (int i = 1; i < entrySize; i++)
        {
            if (i != 4 && bytes[offset + i] != 0)
                return false;
        }

        byte modifier = bytes[offset + 4];
        return (modifier & (byte)NativeMethods.MOD.WIN) != 0 && (modifier & 0xF0) == 0;
    }
}
