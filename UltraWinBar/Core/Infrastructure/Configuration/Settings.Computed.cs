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

        // EnabledEdges/Resolved*Edge used to be recomputed (EnabledEdges allocating a fresh list)
        // on every get, and Tasks_Filter reads them for every window x every panel on each list
        // refresh. Cached here and invalidated from OnPropertyChanged (Settings.cs) whenever the
        // inputs they depend on change, instead of via external subscriptions.
        private List<AppBarEdge> _enabledEdgesCache;
        private AppBarEdge? _resolvedTrayEdgeCache;
        private AppBarEdge? _resolvedClockEdgeCache;
        private AppBarEdge? _resolvedStartButtonEdgeCache;
        private AppBarEdge? _resolvedLanguageEdgeCache;
        private AppBarEdge? _resolvedDefaultTaskEdgeCache;

        /// <summary>
        /// Invalidates the EnabledEdges/Resolved*Edge caches above when a property they're
        /// derived from changes. Called from OnPropertyChanged for every property, so it must
        /// stay cheap for the common case (no match).
        /// </summary>
        private void InvalidateEdgeCaches(string propertyName)
        {
            switch (propertyName)
            {
                case nameof(Edge):
                case nameof(AdditionalEdges):
                    _enabledEdgesCache = null;
                    _resolvedTrayEdgeCache = null;
                    _resolvedClockEdgeCache = null;
                    _resolvedStartButtonEdgeCache = null;
                    _resolvedLanguageEdgeCache = null;
                    _resolvedDefaultTaskEdgeCache = null;
                    break;
                case nameof(TrayEdge):
                    _resolvedTrayEdgeCache = null;
                    break;
                case nameof(ClockEdge):
                    _resolvedClockEdgeCache = null;
                    break;
                case nameof(StartButtonEdge):
                    _resolvedStartButtonEdgeCache = null;
                    break;
                case nameof(LanguageEdge):
                    _resolvedLanguageEdgeCache = null;
                    break;
                case nameof(DefaultTaskEdge):
                    _resolvedDefaultTaskEdgeCache = null;
                    break;
            }
        }

        /// <summary>
        /// Every edge that should have a taskbar: the primary one plus any extras. The returned
        /// list is cached and shared, so it's exposed read-only — callers must not mutate it.
        /// </summary>
        // System.Text.Json serializes read-only collection properties regardless of
        // IgnoreReadOnlyProperties, so this needs an explicit opt-out.
        [JsonIgnore]
        public IReadOnlyList<AppBarEdge> EnabledEdges
        {
            get
            {
                if (_enabledEdgesCache == null)
                {
                    List<AppBarEdge> edges = [Edge];

                    foreach (AppBarEdge edge in AdditionalEdges)
                    {
                        if (!edges.Contains(edge))
                        {
                            edges.Add(edge);
                        }
                    }

                    _enabledEdgesCache = edges;
                }

                return _enabledEdgesCache;
            }
        }

        /// <summary>
        /// TrayEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedTrayEdge => _resolvedTrayEdgeCache ??= EnabledEdges.Contains(TrayEdge) ? TrayEdge : Edge;

        /// <summary>
        /// ClockEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedClockEdge => _resolvedClockEdgeCache ??= EnabledEdges.Contains(ClockEdge) ? ClockEdge : Edge;

        /// <summary>
        /// StartButtonEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedStartButtonEdge => _resolvedStartButtonEdgeCache ??= EnabledEdges.Contains(StartButtonEdge) ? StartButtonEdge : Edge;

        /// <summary>
        /// LanguageEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedLanguageEdge => _resolvedLanguageEdgeCache ??= EnabledEdges.Contains(LanguageEdge) ? LanguageEdge : Edge;

        /// <summary>
        /// DefaultTaskEdge, falling back to the primary edge if that taskbar isn't open.
        /// </summary>
        public AppBarEdge ResolvedDefaultTaskEdge => _resolvedDefaultTaskEdgeCache ??= EnabledEdges.Contains(DefaultTaskEdge) ? DefaultTaskEdge : Edge;

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
                IReadOnlyList<AppBarEdge> enabled = EnabledEdges;
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
        /// Drops TaskOrder entries for window lifetimes that no longer exist, across every
        /// edge/desktop scope at once (liveness is global, unlike the per-edge order itself).
        /// No-op if nothing is stale.
        /// </summary>
        public void PruneDeadTaskOrderEntries(HashSet<string> liveWindowKeys)
        {
            List<TaskOrderEntry> pruned = TaskOrderIdentifier.PruneDeadWindowEntries(TaskOrder, liveWindowKeys);
            if (pruned != null)
            {
                TaskOrder = pruned;
            }
        }

        /// <summary>
        /// Drops per-window TaskbarAssignments (Ctrl+drag, desktop moves) for window lifetimes
        /// that no longer exist. No-op if nothing is stale.
        /// </summary>
        public void PruneDeadTaskbarAssignments(HashSet<string> liveWindowKeys)
        {
            List<TaskbarAssignment> pruned = TaskOrderIdentifier.PruneDeadWindowAssignments(TaskbarAssignments, liveWindowKeys);
            if (pruned != null)
            {
                TaskbarAssignments = pruned;
            }
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
