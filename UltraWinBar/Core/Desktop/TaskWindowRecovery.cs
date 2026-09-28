using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    internal sealed class TaskWindowRecovery : IDisposable
    {
        private readonly TasksService service;
        private readonly IList<ApplicationWindow> windows;
        private readonly VirtualDesktopContext desktops;
        private readonly WinEventHook cloakHook;
        private readonly HashSet<IntPtr> pending = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> cloakChanged = new HashSet<IntPtr>();
        private bool queued;
        private bool disposed;

        internal TaskWindowRecovery(Tasks tasks, TasksService service, VirtualDesktopContext desktops)
        {
            this.service = service;
            this.desktops = desktops;
            windows = tasks.GroupedWindows.SourceCollection as IList<ApplicationWindow>
                ?? throw new InvalidOperationException("Task window source is not mutable.");
            cloakHook = new WinEventHook("Task recovery cloak hook", EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED,
                OnCloakChanged, WinEventHook.SkipOwnProcess);
            desktops.Changed += DesktopChanged;
            desktops.ManagerRecovered += ManagerRecovered;
            EnumerateCurrentWindows();
        }

        private void DesktopChanged(object sender, EventArgs e) => EnumerateCurrentWindows();

        // Manager was null (and callers saw stale IsOnCurrentDesktop=true) until now; re-filter.
        private void ManagerRecovered(object sender, EventArgs e)
        {
            foreach (var panel in Application.Current.Windows.OfType<Taskbar>())
                panel.TaskListControl.RefreshWindowVisibility();
        }

        private void EnumerateCurrentWindows()
        {
            EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd)) pending.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            Queue();
        }

        private const uint EVENT_OBJECT_CLOAKED = 0x8017;
        private const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

        // Cloak flips when a window moves between desktops; uncloaked windows may also be missing from the task source.
        private void OnCloakChanged(uint type, IntPtr hwnd, int obj, int child)
        {
            if (disposed || hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            if (type == EVENT_OBJECT_UNCLOAKED) pending.Add(hwnd);
            cloakChanged.Add(hwnd);
            Queue();
        }

        private void Queue()
        {
            if (queued || disposed) return;
            queued = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                queued = false;
                if (disposed) return;
                if (desktops.RefreshCurrent())
                {
                    // Registry lagged the cloak events; Changed already queued a fresh batch that needs pending.
                    return;
                }
                var handles = pending.ToArray();
                pending.Clear();
                var tracked = new Dictionary<IntPtr, ApplicationWindow>();
                foreach (var window in windows) tracked[window.Handle] = window;
                var moved = new List<ApplicationWindow>();
                foreach (var hwnd in cloakChanged)
                {
                    desktops.ForgetWindowDesktop(hwnd);
                    if (tracked.TryGetValue(hwnd, out var window)) moved.Add(window);
                }
                cloakChanged.Clear();
                bool windowAdded = false;
                bool showInTaskbarChanged = false;
                foreach (var hwnd in handles)
                {
                    if (!IsWindow(hwnd)) continue;
                    if (tracked.TryGetValue(hwnd, out var existing))
                    {
                        bool before = existing.ShowInTaskbar;
                        existing.SetShowInTaskbar();
                        if (existing.ShowInTaskbar != before) showInTaskbarChanged = true;
                        continue;
                    }
                    if (!CanPossiblyAddToTaskbar(hwnd)) continue;
                    var candidate = new ApplicationWindow(service, hwnd);
                    if (candidate.CanAddToTaskbar)
                    {
                        candidate.SetShowInTaskbar();
                        windows.Add(candidate);
                        windowAdded = true;
                        ShellLogger.Info($"Task recovery: restored window {hwnd}");
                    }
                    else candidate.Dispose();
                }
                if (RequiresPanelRefresh(windowAdded, showInTaskbarChanged))
                {
                    foreach (var panel in Application.Current.Windows.OfType<Taskbar>())
                        panel.TaskListControl.RefreshWindowVisibility();
                }
                else if (moved.Count > 0)
                {
                    foreach (var panel in Application.Current.Windows.OfType<Taskbar>())
                        foreach (var window in moved) panel.TaskListControl.ReevaluateWindow(window);
                }
            }), DispatcherPriority.Background);
        }

        internal static bool RequiresPanelRefresh(bool windowAdded, bool showInTaskbarChanged) =>
            windowAdded || showInTaskbarChanged;

        // Mirrors ApplicationWindow.CanAddToTaskbar's Win32 checks to skip constructing one for windows it would reject anyway.
        private static bool CanPossiblyAddToTaskbar(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd)) return false;
            int extendedStyle = GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
            bool hasNoOwner = GetWindow(hwnd, GetWindow_Cmd.GW_OWNER) == IntPtr.Zero;
            bool taskListNotDeleted = GetProp(hwnd, "ITaskList_Deleted") == IntPtr.Zero;
            return CanStyleAddToTaskbar(extendedStyle, hasNoOwner, taskListNotDeleted);
        }

        internal static bool CanStyleAddToTaskbar(int extendedStyle, bool hasNoOwner, bool taskListNotDeleted)
        {
            bool isAppWindow = (extendedStyle & (int)ExtendedWindowStyles.WS_EX_APPWINDOW) != 0;
            bool isToolWindow = (extendedStyle & (int)ExtendedWindowStyles.WS_EX_TOOLWINDOW) != 0;
            bool isNoActivate = (extendedStyle & (int)ExtendedWindowStyles.WS_EX_NOACTIVATE) != 0;
            return (hasNoOwner || isAppWindow) && (!isNoActivate || isAppWindow) && !isToolWindow && taskListNotDeleted;
        }

        public void Dispose()
        {
            disposed = true;
            desktops.Changed -= DesktopChanged;
            desktops.ManagerRecovered -= ManagerRecovered;
            cloakHook.Dispose();
            pending.Clear();
            cloakChanged.Clear();
        }
    }
}
