using ManagedShell.AppBar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace UltraWinBar.Utilities
{
    internal partial class Settings
    {
        #region Computed helpers
        // Read-only, so these are not written to UltraWinBar.settings.json.

        /// <summary>
        /// Every edge that should have a taskbar: the primary one plus any extras.
        /// </summary>
        // System.Text.Json serializes read-only collection properties regardless of
        // IgnoreReadOnlyProperties, so this needs an explicit opt-out.
        [JsonIgnore]
        public List<AppBarEdge> EnabledEdges
        {
            get
            {
                List<AppBarEdge> edges = [Edge];

                foreach (AppBarEdge edge in AdditionalEdges)
                {
                    if (!edges.Contains(edge))
                    {
                        edges.Add(edge);
                    }
                }

                return edges;
            }
        }

        /// <summary>
        /// TrayEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedTrayEdge => EnabledEdges.Contains(TrayEdge) ? TrayEdge : Edge;

        /// <summary>
        /// ClockEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedClockEdge => EnabledEdges.Contains(ClockEdge) ? ClockEdge : Edge;

        /// <summary>
        /// StartButtonEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedStartButtonEdge => EnabledEdges.Contains(StartButtonEdge) ? StartButtonEdge : Edge;

        /// <summary>
        /// LanguageEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedLanguageEdge => EnabledEdges.Contains(LanguageEdge) ? LanguageEdge : Edge;

        /// <summary>
        /// DefaultTaskEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedDefaultTaskEdge => EnabledEdges.Contains(DefaultTaskEdge) ? DefaultTaskEdge : Edge;

        /// <summary>
        /// The order taskbars should register with the OS AppBar API on each screen.
        /// Edges earlier in this list keep their full length; later ones get shrunk by
        /// the OS to avoid overlapping earlier ones. The default advances from the
        /// top-left origin toward the far right and bottom boundaries.
        /// </summary>
        [JsonIgnore]
        public List<AppBarEdge> ResolvedEdgeOrder
        {
            get
            {
                List<AppBarEdge> enabled = EnabledEdges;
                List<AppBarEdge> order = [];

                foreach (AppBarEdge edge in EdgePriority)
                {
                    if (enabled.Contains(edge) && !order.Contains(edge))
                    {
                        order.Add(edge);
                    }
                }

                foreach (AppBarEdge edge in new[] { AppBarEdge.Left, AppBarEdge.Top, AppBarEdge.Right, AppBarEdge.Bottom })
                {
                    if (enabled.Contains(edge) && !order.Contains(edge))
                    {
                        order.Add(edge);
                    }
                }

                return order;
            }
        }

        /// <summary>
        /// Per-edge taskbar size (row count or column count, depending on orientation),
        /// falling back to the given default if that edge has no override yet.
        /// </summary>
        public int GetEdgeSize(AppBarEdge edge, int defaultValue)
        {
            foreach (EdgeSizeSetting entry in EdgeSizes)
            {
                if (entry.Edge == edge)
                {
                    return entry.Size;
                }
            }

            return defaultValue;
        }

        public void SetEdgeSize(AppBarEdge edge, int value)
        {
            List<EdgeSizeSetting> edges = new List<EdgeSizeSetting>(EdgeSizes);
            int index = edges.FindIndex(entry => entry.Edge == edge);

            if (index >= 0)
            {
                EdgeSizeSetting entry = edges[index];
                entry.Size = value;
                edges[index] = entry;
            }
            else
            {
                edges.Add(new EdgeSizeSetting { Edge = edge, Size = value });
            }

            EdgeSizes = edges;
            OnPropertyChanged(nameof(PrimaryRowCount));
            OnPropertyChanged(nameof(PrimaryTaskbarWidth));
        }

        /// <summary>
        /// The saved task button order for one edge, as a list of identifiers.
        /// </summary>
        public List<string> GetTaskOrderForEdge(AppBarEdge edge, Guid? desktopId = null)
        {
            Guid desktop = desktopId ?? VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty;
            bool hasScopedOrder = TaskOrder.Any(entry => entry.Edge == edge && entry.DesktopId == desktop);
            List<string> result = new List<string>();

            foreach (TaskOrderEntry entry in TaskOrder)
            {
                if (entry.Edge == edge && entry.DesktopId == (hasScopedOrder ? desktop : Guid.Empty))
                {
                    result.Add(entry.Identifier);
                }
            }

            return result;
        }

        /// <summary>
        /// Replaces the saved order for one edge, preserving other edges' entries untouched.
        /// </summary>
        public void SetTaskOrderForEdge(AppBarEdge edge, List<string> identifiers, Guid? desktopId = null)
        {
            Guid desktop = desktopId ?? VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty;
            List<TaskOrderEntry> entries = new List<TaskOrderEntry>();

            foreach (TaskOrderEntry entry in TaskOrder)
            {
                if (entry.Edge != edge || entry.DesktopId != desktop)
                {
                    entries.Add(entry);
                }
            }

            foreach (string identifier in identifiers)
            {
                entries.Add(new TaskOrderEntry { Edge = edge, Identifier = identifier, DesktopId = desktop });
            }

            // TaskOrder is a List<T>, so Set<T>'s field.Equals(value) is reference equality —
            // assigning a content-identical new list would still raise PropertyChanged. That
            // feeds back into TaskList's TaskOrder->Refresh()->GroupedWindows_CollectionChanged
            // ->SaveTaskOrder()->SetTaskOrderForEdge loop forever. Skip the write when the
            // content hasn't actually changed (TaskOrderEntry is a struct, so SequenceEqual
            // uses structural equality here).
            if (entries.SequenceEqual(TaskOrder))
            {
                return;
            }

            TaskOrder = entries;
        }

        /// <summary>
        /// Which taskbar a Quick Launch shortcut is shown on, falling back to the "main"
        /// taskbar (see DefaultTaskEdge / "Make main") if it hasn't been moved yet.
        /// </summary>
        public AppBarEdge GetQuickLaunchEdge(string path)
        {
            foreach (QuickLaunchAssignment assignment in QuickLaunchAssignments)
            {
                if (assignment.Path == path)
                {
                    return EnabledEdges.Contains(assignment.Edge) ? assignment.Edge : ResolvedDefaultTaskEdge;
                }
            }

            return ResolvedDefaultTaskEdge;
        }

        public void SetQuickLaunchEdge(string path, AppBarEdge edge)
        {
            List<QuickLaunchAssignment> assignments = new List<QuickLaunchAssignment>(QuickLaunchAssignments);
            int index = assignments.FindIndex(assignment => assignment.Path == path);

            if (index >= 0)
            {
                QuickLaunchAssignment assignment = assignments[index];
                assignment.Edge = edge;
                assignments[index] = assignment;
            }
            else
            {
                assignments.Add(new QuickLaunchAssignment { Path = path, Edge = edge });
            }

            QuickLaunchAssignments = assignments;
        }

        /// <summary>
        /// The primary edge's own row count (Properties dialog binds to this, not the
        /// global RowCount default, so editing it never affects other edges' panels).
        /// </summary>
        // Computed pass-through over EdgeSizes — must not be serialized separately.
        [JsonIgnore]
        public int PrimaryRowCount
        {
            get => GetEdgeSize(Edge, RowCount);
            set => SetEdgeSize(Edge, value);
        }

        /// <summary>
        /// The primary edge's own width. See <see cref="PrimaryRowCount"/>.
        /// </summary>
        [JsonIgnore]
        public int PrimaryTaskbarWidth
        {
            get => GetEdgeSize(Edge, TaskbarWidth);
            set => SetEdgeSize(Edge, value);
        }
        #endregion
    }
}
