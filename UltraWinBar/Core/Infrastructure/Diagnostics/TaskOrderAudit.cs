using ManagedShell.AppBar;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    // One log line per change of the stored task order: which desktop and edge was written, what was added, removed
    // or moved, and the other desktops' orders that aligning shared keys touched, so "the order changed" can be traced.
    internal static class TaskOrderAudit
    {
        private const int MaxKeys = 8;

        internal static string Describe(IReadOnlyList<TaskOrderEntry> before, IReadOnlyList<TaskOrderEntry> after, AppBarEdge edge, Guid written)
        {
            var parts = new List<string>();
            foreach (Guid scope in before.Concat(after).Where(e => e.Edge == edge).Select(e => e.DesktopId).Distinct().OrderBy(id => id != written))
            {
                string change = DescribeScope(Keys(before, edge, scope), Keys(after, edge, scope));
                if (change != null) parts.Add($"{(scope == written ? "written" : "aligned")} {Short(scope)}: {change}");
            }
            return parts.Count == 0 ? null : $"Task order: edge={edge}; " + string.Join("; ", parts);
        }

        // Entries dropped by pruning dead window keys, per edge and desktop.
        internal static string DescribePrune(IReadOnlyList<TaskOrderEntry> before, IReadOnlyList<TaskOrderEntry> after)
        {
            var parts = new List<string>();
            foreach (var scope in before.Select(e => (e.Edge, e.DesktopId)).Distinct())
            {
                var removed = Keys(before, scope.Edge, scope.DesktopId).Except(Keys(after, scope.Edge, scope.DesktopId)).ToList();
                if (removed.Count > 0) parts.Add($"edge={scope.Edge} {Short(scope.DesktopId)}: -{Format(removed)}");
            }
            return parts.Count == 0 ? null : "Task order pruned: " + string.Join("; ", parts);
        }

        private static List<string> Keys(IReadOnlyList<TaskOrderEntry> entries, AppBarEdge edge, Guid scope) =>
            entries.Where(e => e.Edge == edge && e.DesktopId == scope && e.Identifier != null).Select(e => e.Identifier).Distinct().ToList();

        private static string DescribeScope(List<string> before, List<string> after)
        {
            var added = after.Except(before).ToList();
            var removed = before.Except(after).ToList();
            var keptBefore = before.Where(after.Contains).ToList();
            var keptAfter = after.Where(before.Contains).ToList();
            var moved = keptBefore.Select((key, i) => (key, was: i, now: keptAfter.IndexOf(key))).Where(m => m.was != m.now).ToList();

            var changes = new List<string>();
            if (added.Count > 0) changes.Add($"+{Format(added)}");
            if (removed.Count > 0) changes.Add($"-{Format(removed)}");
            if (moved.Count > 0) changes.Add($"moved={moved.Count} {Format(moved.Select(m => $"{Short(m.key)} {m.was}>{m.now}").ToList(), false)}");
            return changes.Count == 0 ? null : string.Join(" ", changes);
        }

        private static string Format(List<string> keys, bool shorten = true)
        {
            var shown = keys.Take(MaxKeys).Select(key => shorten ? Short(key) : key);
            return "[" + string.Join(",", shown) + (keys.Count > MaxKeys ? $",...{keys.Count - MaxKeys} more" : "") + "]";
        }

        private static string Short(Guid scope) => scope.ToString("N").Substring(0, 8);

        // window:v2:<pid>:<start>:<hwnd> -> w<pid>:<hwnd>; application keys keep the file name and ordinal.
        internal static string Short(string key)
        {
            if (TaskOrderIdentifier.TryParseWindowKey(key, out uint processId, out long handle))
                return $"w{processId}:{handle:X}";
            string prefix = key.StartsWith("pin:", StringComparison.Ordinal) ? "pin:" : "";
            int cut = Math.Max(key.LastIndexOf('\\'), key.LastIndexOf('/'));
            string name = cut >= 0 ? key.Substring(cut + 1) : key;
            return (prefix + name).Length > 40 ? (prefix + name).Substring(0, 40) : prefix + name;
        }
    }
}
