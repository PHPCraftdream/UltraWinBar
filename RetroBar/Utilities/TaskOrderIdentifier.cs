using ManagedShell.WindowsTasks;

namespace RetroBar.Utilities
{
    /// <summary>
    /// Identifier used for persisting/restoring task button order. Plain
    /// TaskAssignmentManager.GetIdentifier(window, WindowClassAndTitle) is unusable here —
    /// it embeds the full window title, and some apps (e.g. terminals showing live CPU/RAM
    /// in their title) change it every few seconds, so the "same" window never matches its
    /// own saved entry. Falls back to grouping by executable plus the window's ordinal
    /// position among its own same-executable siblings (in Tasks.GroupedWindows' stable,
    /// un-sorted-by-us natural order) — stable across a restart as long as those windows are
    /// still running, since neither the executable nor sibling discovery order depends on title.
    /// </summary>
    public static class TaskOrderIdentifier
    {
        public static string Get(ApplicationWindow window, Tasks tasks)
        {
            string appId = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            if (appId == null || tasks == null)
            {
                return TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.WindowClassAndTitle);
            }

            int ordinal = 0;
            foreach (object item in tasks.GroupedWindows)
            {
                if (item is ApplicationWindow sibling &&
                    TaskAssignmentManager.GetIdentifier(sibling, TaskAssignmentMode.ExecutablePath) == appId)
                {
                    ordinal++;
                    if (ReferenceEquals(sibling, window))
                    {
                        break;
                    }
                }
            }

            return appId + "#" + ordinal;
        }
    }
}
