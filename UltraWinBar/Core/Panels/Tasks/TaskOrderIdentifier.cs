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
