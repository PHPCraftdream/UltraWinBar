using ManagedShell.AppBar;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// Remembers which taskbar a window or application was dragged to, so it keeps
    /// opening there across restarts. A plain drag assigns the whole application (keyed by
    /// executable/AppUserModelID); Ctrl+drag assigns just that window by its lifetime
    /// identity. Persisted in Settings.TaskbarAssignments.
    /// </summary>
    public static class TaskAssignmentManager
    {
        /// <summary>
        /// The identifier used to remember/look up this window's assigned taskbar under the
        /// given mode. Returns null if the window can't be identified that way.
        /// </summary>
        public static string GetIdentifier(ApplicationWindow window, TaskAssignmentMode mode)
        {
            if (window == null)
            {
                return null;
            }

            if (mode == TaskAssignmentMode.WindowClassAndTitle)
            {
                return TaskOrderIdentifier.Get(window, null);
            }

            // ExecutablePath mode: group all windows of the same application together.
            return GetExecutableIdentifier(window);
        }

        // Cached per window: called in nested loops (pin matching, legacy ordinal scan, per-panel
        // edge assignment). Validated against the source values rather than window identity alone,
        // since a UWP window's AppUserModelID can arrive empty and be filled in later. Reads
        // AppUserModelID/WinFileName as lazily as the original formula did (both properties do
        // real work on a miss), not unconditionally.
        private sealed class ExecutableIdentity
        {
            internal bool IsUWP;
            internal string AppUserModelID;
            internal string WinFileName;
            internal string Key;
            internal bool Computed;
        }

        private static readonly ConditionalWeakTable<ApplicationWindow, ExecutableIdentity> executableIdentities = new();

        private static string GetExecutableIdentifier(ApplicationWindow window)
        {
            bool isUwp = window.IsUWP;
            string aumid = isUwp ? window.AppUserModelID : null;
            bool hasUwpId = isUwp && !string.IsNullOrEmpty(aumid);
            string winFileName = hasUwpId ? null : window.WinFileName;

            ExecutableIdentity identity = executableIdentities.GetValue(window, _ => new ExecutableIdentity());
            if (!identity.Computed || identity.IsUWP != isUwp || identity.AppUserModelID != aumid || identity.WinFileName != winFileName)
            {
                identity.IsUWP = isUwp;
                identity.AppUserModelID = aumid;
                identity.WinFileName = winFileName;
                identity.Key = hasUwpId ? $"uwp:{aumid}" : (!string.IsNullOrEmpty(winFileName) ? $"exe:{winFileName}" : null);
                identity.Computed = true;
            }

            return identity.Key;
        }

        /// <summary>
        /// The edge this window is assigned to, or null if it has no assignment (in which
        /// case it belongs on the default taskbar). Checks the window-specific assignment
        /// first, then falls back to legacy title-specific and application-wide rules.
        /// </summary>
        public static AppBarEdge? GetAssignedEdge(ApplicationWindow window)
        {
            string windowId = GetIdentifier(window, TaskAssignmentMode.WindowClassAndTitle);
            string legacyWindowId = GetLegacyWindowIdentifier(window);
            string appId = GetIdentifier(window, TaskAssignmentMode.ExecutablePath);

            Guid desktop = VirtualDesktopContext.Instance?.DesktopForWindowCached(window.Handle) ?? Guid.Empty;
            return ResolveEdgeIndexed(Settings.Instance.TaskbarAssignments, desktop, windowId, legacyWindowId, appId);
        }

        public static AppBarEdge? ResolveEdge(IEnumerable<TaskbarAssignment> assignments, Guid desktop, string windowId, string appId)
        {
            bool isLegacyWindowId = windowId != null && windowId.StartsWith("class:", StringComparison.Ordinal);
            return ResolveEdge(assignments, desktop, isLegacyWindowId ? null : windowId,
                isLegacyWindowId ? windowId : null, appId);
        }

        public static AppBarEdge? ResolveEdge(IEnumerable<TaskbarAssignment> assignments, Guid desktop, string windowId, string legacyWindowId, string appId)
        {
            AppBarEdge? edge = null;
            int bestScore = 0;

            foreach (var assignment in assignments)
            {
                ApplyCandidate(assignment, desktop, windowId, legacyWindowId, appId, ref bestScore, ref edge);
            }

            return edge;
        }

        /// <summary>
        /// Scores one assignment against the three candidate identifiers and, if it's the new
        /// best match, updates bestScore/edge. Shared by the linear ResolveEdge (used directly
        /// by tests) and the indexed lookup below, so both apply identical desktop/mode/score
        /// semantics.
        /// </summary>
        private static void ApplyCandidate(TaskbarAssignment assignment, Guid desktop, string windowId, string legacyWindowId, string appId,
            ref int bestScore, ref AppBarEdge? edge)
        {
            if (assignment.DesktopId != desktop && assignment.DesktopId != Guid.Empty) return;
            int scopeScore = assignment.DesktopId == desktop ? 4 : 0;
            if (assignment.Mode == TaskAssignmentMode.WindowClassAndTitle && windowId != null && assignment.Identifier == windowId)
            {
                if (scopeScore + 3 >= bestScore) { bestScore = scopeScore + 3; edge = assignment.Edge; }
            }
            else if (assignment.Mode == TaskAssignmentMode.WindowClassAndTitle && legacyWindowId != null && assignment.Identifier == legacyWindowId)
            {
                if (scopeScore + 2 >= bestScore) { bestScore = scopeScore + 2; edge = assignment.Edge; }
            }
            else if (assignment.Mode == TaskAssignmentMode.ExecutablePath && appId != null && assignment.Identifier == appId)
            {
                if (scopeScore + 1 >= bestScore) { bestScore = scopeScore + 1; edge = assignment.Edge; }
            }
        }

        // Groups a TaskbarAssignments list by Identifier, preserving each identifier's original
        // relative order, so GetAssignedEdge only has to look at the 1-3 candidate identifiers
        // instead of scanning every assignment. Keyed by the List<TaskbarAssignment> instance
        // itself: Settings always replaces that list wholesale on any change (see
        // Settings.TaskbarAssignments/PruneDeadTaskbarAssignments), so a stale index is never
        // reused for changed content, and the entry is collected once the list is replaced.
        private static readonly ConditionalWeakTable<List<TaskbarAssignment>, Dictionary<string, List<(int Index, TaskbarAssignment Assignment)>>> assignmentIndexes = new();

        private static Dictionary<string, List<(int Index, TaskbarAssignment Assignment)>> GetAssignmentIndex(List<TaskbarAssignment> assignments)
        {
            return assignmentIndexes.GetValue(assignments, list =>
            {
                var index = new Dictionary<string, List<(int Index, TaskbarAssignment Assignment)>>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    string identifier = list[i].Identifier;
                    if (identifier == null) continue;

                    if (!index.TryGetValue(identifier, out var bucket))
                    {
                        bucket = new List<(int, TaskbarAssignment)>();
                        index[identifier] = bucket;
                    }

                    bucket.Add((i, list[i]));
                }

                return index;
            });
        }

        /// <summary>
        /// Same result as ResolveEdge(assignments, ...), but only visits assignments whose
        /// Identifier matches one of the (up to 3) candidate identifiers, via a per-list index.
        /// Candidates are folded in their original list order so ties resolve identically to the
        /// linear scan (later entries win).
        /// </summary>
        private static AppBarEdge? ResolveEdgeIndexed(List<TaskbarAssignment> assignments, Guid desktop, string windowId, string legacyWindowId, string appId)
        {
            if (assignments == null || assignments.Count == 0) return null;

            var index = GetAssignmentIndex(assignments);
            Dictionary<int, TaskbarAssignment> candidates = null;

            void Collect(string identifier)
            {
                if (identifier == null || !index.TryGetValue(identifier, out var bucket)) return;
                candidates ??= new Dictionary<int, TaskbarAssignment>();
                foreach (var (i, assignment) in bucket)
                {
                    candidates[i] = assignment;
                }
            }

            Collect(windowId);
            Collect(legacyWindowId);
            Collect(appId);

            if (candidates == null) return null;

            AppBarEdge? edge = null;
            int bestScore = 0;
            foreach (int i in candidates.Keys.OrderBy(i => i))
            {
                ApplyCandidate(candidates[i], desktop, windowId, legacyWindowId, appId, ref bestScore, ref edge);
            }

            return edge;
        }

        /// <summary>
        /// Pins the window (or its whole application, per mode) to the given edge, replacing
        /// any previous assignment made under that same mode. No-ops if unidentifiable.
        /// </summary>
        public static void AssignToEdge(ApplicationWindow window, AppBarEdge edge, TaskAssignmentMode mode)
        {
            string identifier = GetIdentifier(window, mode);
            string legacyWindowId = mode == TaskAssignmentMode.WindowClassAndTitle ? GetLegacyWindowIdentifier(window) : null;
            Guid desktop = VirtualDesktopContext.Instance?.DesktopForWindow(window.Handle) ?? Guid.Empty;

            if (identifier == null)
            {
                return;
            }

            List<TaskbarAssignment> assignments = Settings.Instance.TaskbarAssignments
                .Where(a => !(a.Mode == mode && a.DesktopId == desktop &&
                    (a.Identifier == identifier || (legacyWindowId != null && a.Identifier == legacyWindowId))))
                .ToList();

            assignments.Add(new TaskbarAssignment { Identifier = identifier, Edge = edge, Mode = mode, DesktopId = desktop });

            // Assigning a new list instance triggers persistence and notifies open taskbars.
            Settings.Instance.TaskbarAssignments = assignments;
        }

        /// <summary>
        /// Forgets both the window-specific and application-wide assignment for this window,
        /// so it falls back to the default taskbar again.
        /// </summary>
        public static void ResetAssignment(ApplicationWindow window)
        {
            string windowId = GetIdentifier(window, TaskAssignmentMode.WindowClassAndTitle);
            string legacyWindowId = GetLegacyWindowIdentifier(window);
            string appId = GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            Guid desktop = VirtualDesktopContext.Instance?.DesktopForWindow(window.Handle) ?? Guid.Empty;

            List<TaskbarAssignment> assignments = Settings.Instance.TaskbarAssignments
                .Where(a => a.DesktopId != desktop ||
                    (!(a.Mode == TaskAssignmentMode.WindowClassAndTitle && windowId != null && a.Identifier == windowId) &&
                     !(a.Mode == TaskAssignmentMode.WindowClassAndTitle && legacyWindowId != null && a.Identifier == legacyWindowId) &&
                     !(a.Mode == TaskAssignmentMode.ExecutablePath && appId != null && a.Identifier == appId)))
                .ToList();

            if (appId != null) assignments.Add(new TaskbarAssignment { Identifier = appId,
                Mode = TaskAssignmentMode.ExecutablePath, DesktopId = desktop, Edge = Settings.Instance.ResolvedDefaultTaskEdge });

            Settings.Instance.TaskbarAssignments = assignments;
        }

        // Cached per window, validated by reference equality: ApplicationWindow only replaces its
        // ClassName/Title backing fields when the value actually changes, so a reference compare is
        // cheaper than rebuilding the string and correctly detects a real change (titles change often).
        private sealed class LegacyIdentity
        {
            internal string ClassName;
            internal string Title;
            internal string Key;
            internal bool Computed;
        }

        private static readonly ConditionalWeakTable<ApplicationWindow, LegacyIdentity> legacyIdentities = new();

        internal static string GetLegacyWindowIdentifier(ApplicationWindow window)
        {
            if (window == null) return null;

            string className = window.ClassName;
            string title = window.Title;

            LegacyIdentity identity = legacyIdentities.GetValue(window, _ => new LegacyIdentity());
            if (!identity.Computed || !ReferenceEquals(identity.ClassName, className) || !ReferenceEquals(identity.Title, title))
            {
                identity.ClassName = className;
                identity.Title = title;
                identity.Key = string.IsNullOrEmpty(className) && string.IsNullOrEmpty(title) ? null : $"class:{className}|title:{title}";
                identity.Computed = true;
            }

            return identity.Key;
        }
    }
}
