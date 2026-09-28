using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTray;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace UltraWinBar.Utilities
{
    internal partial class Settings
    {
        // Extra taskbars beyond the primary edge.
        private List<AppBarEdge> _additionalEdges = [AppBarEdge.Left, AppBarEdge.Top, AppBarEdge.Bottom];
        public List<AppBarEdge> AdditionalEdges
        {
            get => _additionalEdges;
            set => Set(ref _additionalEdges, value);
        }

        // Taskbar hosting the notification area. There is only ever one.
        private AppBarEdge _trayEdge = AppBarEdge.Bottom;
        public AppBarEdge TrayEdge
        {
            get => _trayEdge;
            set => SetEnum(ref _trayEdge, value);
        }

        // Taskbar hosting the clock. There is only ever one, independent of the tray.
        private AppBarEdge _clockEdge = AppBarEdge.Right;
        public AppBarEdge ClockEdge
        {
            get => _clockEdge;
            set => SetEnum(ref _clockEdge, value);
        }

        // Taskbar hosting the start button. There is only ever one.
        private AppBarEdge _startButtonEdge = AppBarEdge.Right;
        public AppBarEdge StartButtonEdge
        {
            get => _startButtonEdge;
            set => SetEnum(ref _startButtonEdge, value);
        }

        // Taskbar hosting the input language indicator. There is only ever one.
        private AppBarEdge _languageEdge = AppBarEdge.Right;
        public AppBarEdge LanguageEdge
        {
            get => _languageEdge;
            set => SetEnum(ref _languageEdge, value);
        }

        // Taskbar new, unpinned windows open on by default ("Make main" on a taskbar's
        // context menu sets this). Independent of Edge, which is the primary taskbar's
        // physical location, not where unassigned windows land.
        private AppBarEdge _defaultTaskEdge = AppBarEdge.Bottom;
        public AppBarEdge DefaultTaskEdge
        {
            get => _defaultTaskEdge;
            set => SetEnum(ref _defaultTaskEdge, value);
        }

        // Which taskbar each window/application is pinned to, once dragged there by the
        // user (plain drag = that window only, Ctrl+drag = the whole application).
        private List<TaskbarAssignment> _taskbarAssignments = [];
        public List<TaskbarAssignment> TaskbarAssignments
        {
            get => _taskbarAssignments;
            set => Set(ref _taskbarAssignments, value);
        }

        // Per-edge task button order, keyed by TaskAssignmentManager's WindowClassAndTitle
        // identifier. Relative position among entries sharing an Edge defines that edge's
        // order; entries for other edges may be interleaved.
        private List<PinnedApplication> _pinnedApplications = [];
        public List<PinnedApplication> PinnedApplications
        {
            get => _pinnedApplications;
            set => Set(ref _pinnedApplications, value ?? []);
        }

        private List<TaskOrderEntry> _taskOrder = [];
        private List<string> _allDesktopApplications = [];
        public List<string> AllDesktopApplications
        {
            get => _allDesktopApplications;
            set => Set(ref _allDesktopApplications, (value ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList());
        }

        private bool _desktopPinPreferencesInitialized;
        public bool DesktopPinPreferencesInitialized
        {
            get => _desktopPinPreferencesInitialized;
            set => Set(ref _desktopPinPreferencesInitialized, value);
        }

        public List<TaskOrderEntry> TaskOrder
        {
            get => _taskOrder;
            set => Set(ref _taskOrder, value);
        }

        // Which taskbar each Quick Launch shortcut is shown on. Order within an edge is
        // still governed by QuickLaunchOrder below; this only controls edge membership.
        private List<QuickLaunchAssignment> _quickLaunchAssignments = [];
        public List<QuickLaunchAssignment> QuickLaunchAssignments
        {
            get => _quickLaunchAssignments;
            set => Set(ref _quickLaunchAssignments, value);
        }

        // Edges the user has explicitly "stretched", most-recent first. An edge here
        // registers with the OS AppBar API before edges not listed, so it keeps its full
        // length and neighboring taskbars shrink to make room for it instead.
        private List<AppBarEdge> _edgePriority = [AppBarEdge.Top, AppBarEdge.Left];
        public List<AppBarEdge> EdgePriority
        {
            get => _edgePriority;
            set => Set(ref _edgePriority, value);
        }

        // Per-edge taskbar size override. Each edge is either horizontal or vertical, never
        // both, so one Size field covers RowCount (horizontal) or TaskbarWidth (vertical).
        // Edges without an entry fall back to the global RowCount/TaskbarWidth default below.
        private List<EdgeSizeSetting> _edgeSizes =
        [
            new EdgeSizeSetting { Edge = AppBarEdge.Bottom, Size = 1 },
            new EdgeSizeSetting { Edge = AppBarEdge.Right, Size = 2 },
            new EdgeSizeSetting { Edge = AppBarEdge.Left, Size = 2 },
            new EdgeSizeSetting { Edge = AppBarEdge.Top, Size = 1 }
        ];
        public List<EdgeSizeSetting> EdgeSizes
        {
            get => _edgeSizes;
            set => Set(ref _edgeSizes, value);
        }
    }
}
