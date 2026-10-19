using ManagedShell.AppBar;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using ManagedShell.Interop;

namespace UltraWinBar.Utilities
{
    // Window lifetime identity, independent of discovery order and changing titles.
    public static class TaskOrderIdentifier
    {
        public static string Get(ApplicationWindow window, Tasks tasks)
        {
            if (window == null) return null;
            return identities.GetValue(window, CreateIdentity).Key;
        }

        private sealed class Identity { internal string Key; }
        private static readonly ConditionalWeakTable<ApplicationWindow, Identity> identities = new();

        private static Identity CreateIdentity(ApplicationWindow window)
        {
            NativeMethods.GetWindowThreadProcessId(window.Handle, out uint processId);
            long started = 0;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                started = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return new Identity { Key = CreateKey(processId, started, window.Handle.ToInt64()) };
        }

        public static string CreateKey(uint processId, long processStartTicks, long handle) =>
            FormattableString.Invariant($"window:v2:{processId}:{processStartTicks}:{handle:X}");

        /// <summary>
        /// Every window:v2 key currently backed by a real window, across all desktops/edges
        /// (the whole task source, not one panel's filtered view). The single source of truth
        /// for pruning stale TaskOrder/TaskbarAssignments entries and reconciling pin keys.
        /// </summary>
        internal static HashSet<string> LiveKeys(Tasks tasks, IEnumerable<string> storedKeys)
        {
            var keys = new HashSet<string>();
            if (tasks?.GroupedWindows?.SourceCollection != null)
            {
                foreach (ApplicationWindow window in tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>())
                {
                    string key = Get(window, tasks);
                    if (key != null) keys.Add(key);
                }
            }
            // Windows not yet (re)discovered by the task source, e.g. on other desktops right after startup, must keep their order.
            foreach (string key in storedKeys)
                if (key != null && !keys.Contains(key) && WindowStillExists(key)) keys.Add(key);
            return keys;
        }

        internal static bool TryParseWindowKey(string key, out uint processId, out long handle)
        {
            processId = 0;
            handle = 0;
            var parts = key?.Split(':');
            return parts?.Length == 5 && parts[0] == "window" && parts[1] == "v2" &&
                uint.TryParse(parts[2], out processId) &&
                long.TryParse(parts[4], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out handle);
        }

        // internal: also used directly as TaskModelInput.WindowStillExists by TaskModelHost (#128).
        internal static bool WindowStillExists(string key)
        {
            if (!TryParseWindowKey(key, out uint processId, out long handle)) return false;
            var hwnd = new IntPtr(handle);
            if (!NativeMethods.IsWindow(hwnd)) return false;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint actualProcess);
            return actualProcess == processId;
        }

        /// <summary>
        /// Keys shown on several desktops keep one relative order: in every other scope of the edge, the keys it
        /// shares with <paramref name="source"/> (limited to <paramref name="sourceKeys"/>, the ones the source
        /// desktop actually shows) are permuted into the source's order. Their slots are reused, so keys that
        /// belong to one desktop only keep their positions. Edits <paramref name="entries"/> in place.
        /// </summary>
        internal static void AlignSharedOrder(List<TaskOrderEntry> entries, AppBarEdge edge, Guid source, IReadOnlyCollection<string> sourceKeys)
        {
            var rank = new Dictionary<string, int>();
            foreach (var entry in entries)
                if (entry.Edge == edge && entry.DesktopId == source && entry.Identifier != null && sourceKeys.Contains(entry.Identifier))
                    rank.TryAdd(entry.Identifier, rank.Count);
            if (rank.Count < 2) return;

            foreach (Guid scope in entries.Where(e => e.Edge == edge && e.DesktopId != source).Select(e => e.DesktopId).Distinct().ToList())
            {
                var slots = new List<int>();
                var seen = new HashSet<string>();
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry.Edge == edge && entry.DesktopId == scope && entry.Identifier != null && rank.ContainsKey(entry.Identifier) && seen.Add(entry.Identifier))
                        slots.Add(i);
                }
                if (slots.Count < 2) continue;
                var ordered = slots.Select(i => entries[i].Identifier).OrderBy(key => rank[key]).ToList();
                for (int j = 0; j < slots.Count; j++)
                    if (entries[slots[j]].Identifier != ordered[j])
                        entries[slots[j]] = entries[slots[j]] with { Identifier = ordered[j] };
            }
        }

        /// <summary>
        /// A key this desktop has no position for yet (a window shown on all desktops appearing here first) goes
        /// where another desktop keeps it — after its nearest preceding neighbour already ordered here, else before
        /// the nearest following one — instead of at the end. Keys unknown everywhere are left for the caller to append.
        /// </summary>
        internal static void PlaceNewKeys(List<string> order, HashSet<string> orderSet, IEnumerable<string> newKeys, IReadOnlyList<List<string>> otherScopes)
        {
            List<string> ReferenceFor(string key) => otherScopes.FirstOrDefault(scope => scope.Contains(key));
            var pending = newKeys.Where(key => key != null && !orderSet.Contains(key) && ReferenceFor(key) != null).Distinct().ToList();
            while (pending.Count > 0)
            {
                bool placed = false;
                foreach (string key in pending.ToList())
                {
                    List<string> reference = ReferenceFor(key);
                    int at = reference.IndexOf(key);
                    int insertAt = -1;
                    for (int i = at - 1; i >= 0 && insertAt < 0; i--)
                        if (reference[i] != null && orderSet.Contains(reference[i])) insertAt = order.IndexOf(reference[i]) + 1;
                    for (int i = at + 1; i < reference.Count && insertAt < 0; i++)
                        if (reference[i] != null && orderSet.Contains(reference[i])) insertAt = order.IndexOf(reference[i]);
                    if (insertAt < 0) continue;
                    order.Insert(insertAt, key);
                    orderSet.Add(key);
                    pending.Remove(key);
                    placed = true;
                }
                if (placed) continue;
                // No neighbour is ordered here yet: the key that comes first in its reference becomes the anchor.
                string seed = pending.OrderBy(key => ReferenceFor(key).IndexOf(key)).First();
                order.Add(seed);
                orderSet.Add(seed);
                pending.Remove(seed);
            }
        }

        /// <summary>
        /// The stored order after the user rearranged the displayed keys: displayed keys fill the slots displayed
        /// keys already had, so keys of other desktops' windows keep their positions. Displayed keys not stored yet are appended.
        /// </summary>
        internal static List<string> ApplyDisplayedOrder(IReadOnlyList<string> stored, IReadOnlyList<string> displayed)
        {
            var displayedSet = new HashSet<string>(displayed.Where(key => key != null));
            var storedSet = new HashSet<string>(stored.Where(key => key != null));
            var inStored = new Queue<string>(displayed.Where(key => key != null && storedSet.Contains(key)).Distinct());
            var result = new List<string>(stored.Count);
            var placed = new HashSet<string>();
            foreach (string key in stored)
                result.Add(key != null && displayedSet.Contains(key) && placed.Add(key) && inStored.Count > 0 ? inStored.Dequeue() : key);
            foreach (string key in displayed)
                if (key != null && !storedSet.Contains(key) && !result.Contains(key)) result.Add(key);
            return result;
        }

        private static bool IsDeadWindowKey(string identifier, HashSet<string> liveKeys) =>
            identifier != null && identifier.StartsWith("window:v2:", StringComparison.Ordinal) && !liveKeys.Contains(identifier);

        /// <summary>
        /// Removes TaskOrder entries whose window:v2 key no longer has a live window, regardless
        /// of which edge/desktop scope they belong to. Returns null when nothing changed.
        /// </summary>
        internal static List<TaskOrderEntry> PruneDeadWindowEntries(List<TaskOrderEntry> entries, HashSet<string> liveKeys)
        {
            List<TaskOrderEntry> result = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsDeadWindowKey(entries[i].Identifier, liveKeys))
                {
                    result ??= new List<TaskOrderEntry>(entries.Take(i));
                    continue;
                }
                result?.Add(entries[i]);
            }
            return result;
        }

        /// <summary>
        /// Removes per-window TaskbarAssignments whose window:v2 key no longer has a live window.
        /// Legacy class:/exe:/uwp: assignments are untouched. Returns null when nothing changed.
        /// </summary>
        internal static List<TaskbarAssignment> PruneDeadWindowAssignments(List<TaskbarAssignment> assignments, HashSet<string> liveKeys)
        {
            List<TaskbarAssignment> result = null;
            for (int i = 0; i < assignments.Count; i++)
            {
                var assignment = assignments[i];
                bool dead = assignment.Mode == TaskAssignmentMode.WindowClassAndTitle && IsDeadWindowKey(assignment.Identifier, liveKeys);
                if (dead)
                {
                    result ??= new List<TaskbarAssignment>(assignments.Take(i));
                    continue;
                }
                result?.Add(assignment);
            }
            return result;
        }

        /// <summary>
        /// A pin's remembered representative window, kept as-is while it is still live; replaced
        /// with the newly selected window's key only once it is not. Never flips between two live
        /// windows and is idempotent once the stored key matches a live window again.
        /// </summary>
        internal static string ReconcilePrimaryWindowKey(string primaryWindowKey, string selectedWindowKey, HashSet<string> liveKeys)
        {
            if (selectedWindowKey == null || liveKeys.Contains(primaryWindowKey)) return primaryWindowKey;
            return selectedWindowKey;
        }

        public static string GetLegacy(ApplicationWindow window, Tasks tasks)
        {
            if (window == null) return null;
            return GetLegacyBatch(new[] { window }, tasks)[window];
        }

        /// <summary>
        /// GetLegacy for every window in 'windows' at once, via a single pass over
        /// tasks.GroupedWindows.SourceCollection instead of one pass per window (O(W^2) when
        /// many windows need it at once, e.g. startup or an Explorer restart). Reproduces
        /// GetLegacy exactly, window by window, including its edge cases: a window not
        /// ShowInTaskbar (or not found in the source collection at all) gets appId + "#" +
        /// <total matching ShowInTaskbar siblings seen>, same as GetLegacy's loop running to
        /// completion without ever reference-matching it. A transient enumeration failure (e.g.
        /// a sibling window closing mid-scan) falls every pending window back to its bare appId,
        /// same as GetLegacy's own try/catch would for that window individually.
        /// </summary>
        internal static Dictionary<ApplicationWindow, string> GetLegacyBatch(IEnumerable<ApplicationWindow> windows, Tasks tasks)
        {
            var result = new Dictionary<ApplicationWindow, string>();
            var appIds = new Dictionary<ApplicationWindow, string>();
            var pending = new List<ApplicationWindow>();
            foreach (var window in windows)
            {
                if (window == null || result.ContainsKey(window)) continue;

                string appId = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
                if (appId == null || tasks == null)
                {
                    result[window] = TaskAssignmentManager.GetLegacyWindowIdentifier(window);
                }
                else
                {
                    appIds[window] = appId;
                    pending.Add(window);
                }
            }
            if (pending.Count == 0) return result;

            // Best-effort only — never let a transient enumeration failure here (e.g. a
            // sibling window closing mid-loop) affect drag/assignment/sort callers.
            try
            {
                var ordinalAtWindow = new Dictionary<ApplicationWindow, int>();
                var totalByAppId = new Dictionary<string, int>();
                foreach (object item in tasks.GroupedWindows.SourceCollection)
                {
                    if (item is ApplicationWindow sibling && sibling.ShowInTaskbar)
                    {
                        string siblingAppId = TaskAssignmentManager.GetIdentifier(sibling, TaskAssignmentMode.ExecutablePath);
                        if (siblingAppId == null) continue;

                        int ordinal = totalByAppId.TryGetValue(siblingAppId, out int count) ? count + 1 : 1;
                        totalByAppId[siblingAppId] = ordinal;
                        // First occurrence wins, matching GetLegacy's break on the first reference match.
                        if (!ordinalAtWindow.ContainsKey(sibling)) ordinalAtWindow[sibling] = ordinal;
                    }
                }

                foreach (var window in pending)
                {
                    string appId = appIds[window];
                    int ordinal = ordinalAtWindow.TryGetValue(window, out int found) ? found
                        : totalByAppId.TryGetValue(appId, out int total) ? total : 0;
                    result[window] = appId + "#" + ordinal;
                }
            }
            catch (Exception)
            {
                foreach (var window in pending) result[window] = appIds[window];
            }

            return result;
        }
    }
}
