using ManagedShell.AppBar;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// Remembers which taskbar a window or application was dragged to, so it keeps
    /// opening there across restarts. A plain drag assigns the whole application (keyed by
    /// executable/AppUserModelID), so every window it opens shares one taskbar; Ctrl+drag
    /// assigns just that one window (keyed by class+title). Both kinds of assignment can
    /// coexist for the same application; the window-specific one wins since it's more
    /// specific. Persisted in Settings.TaskbarAssignments.
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
                string className = window.ClassName;
                string title = window.Title;

                if (string.IsNullOrEmpty(className) && string.IsNullOrEmpty(title))
                {
                    return null;
                }

                return $"class:{className}|title:{title}";
            }

            // ExecutablePath mode: group all windows of the same application together.
            if (window.IsUWP && !string.IsNullOrEmpty(window.AppUserModelID))
            {
                return $"uwp:{window.AppUserModelID}";
            }

            if (!string.IsNullOrEmpty(window.WinFileName))
            {
                return $"exe:{window.WinFileName}";
            }

            return null;
        }

        /// <summary>
        /// The edge this window is assigned to, or null if it has no assignment (in which
        /// case it belongs on the default taskbar). Checks the window-specific assignment
        /// first, then falls back to the application-wide one.
        /// </summary>
        public static AppBarEdge? GetAssignedEdge(ApplicationWindow window)
        {
            string windowId = GetIdentifier(window, TaskAssignmentMode.WindowClassAndTitle);
            string appId = GetIdentifier(window, TaskAssignmentMode.ExecutablePath);

            Guid desktop = VirtualDesktopContext.Instance?.DesktopForWindow(window.Handle) ?? Guid.Empty;
            return ResolveEdge(Settings.Instance.TaskbarAssignments, desktop, windowId, appId);
        }

        public static AppBarEdge? ResolveEdge(IEnumerable<TaskbarAssignment> assignments, Guid desktop, string windowId, string appId)
        {
            AppBarEdge? edge = null;
            int bestScore = 0;

            foreach (var assignment in assignments)
            {
                if (assignment.DesktopId != desktop && assignment.DesktopId != Guid.Empty) continue;
                int scopeScore = assignment.DesktopId == desktop ? 4 : 0;
                if (assignment.Mode == TaskAssignmentMode.WindowClassAndTitle && windowId != null && assignment.Identifier == windowId)
                {
                    if (scopeScore + 2 >= bestScore) { bestScore = scopeScore + 2; edge = assignment.Edge; }
                }
                else if (assignment.Mode == TaskAssignmentMode.ExecutablePath && appId != null && assignment.Identifier == appId)
                {
                    if (scopeScore + 1 >= bestScore) { bestScore = scopeScore + 1; edge = assignment.Edge; }
                }
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
            Guid desktop = VirtualDesktopContext.Instance?.DesktopForWindow(window.Handle) ?? Guid.Empty;

            if (identifier == null)
            {
                return;
            }

            List<TaskbarAssignment> assignments = Settings.Instance.TaskbarAssignments
                .Where(a => !(a.Mode == mode && a.Identifier == identifier && a.DesktopId == desktop))
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
            string appId = GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            Guid desktop = VirtualDesktopContext.Instance?.DesktopForWindow(window.Handle) ?? Guid.Empty;

            List<TaskbarAssignment> assignments = Settings.Instance.TaskbarAssignments
                .Where(a => a.DesktopId != desktop ||
                    (!(a.Mode == TaskAssignmentMode.WindowClassAndTitle && windowId != null && a.Identifier == windowId) &&
                     !(a.Mode == TaskAssignmentMode.ExecutablePath && appId != null && a.Identifier == appId)))
                .ToList();

            if (appId != null) assignments.Add(new TaskbarAssignment { Identifier = appId,
                Mode = TaskAssignmentMode.ExecutablePath, DesktopId = desktop, Edge = Settings.Instance.ResolvedDefaultTaskEdge });

            Settings.Instance.TaskbarAssignments = assignments;
        }
    }
}
