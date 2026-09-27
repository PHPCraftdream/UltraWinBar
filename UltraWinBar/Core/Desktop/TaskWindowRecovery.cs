using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    internal sealed class TaskWindowRecovery : IDisposable
    {
        private delegate void EventProc(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);
        private readonly TasksService service;
        private readonly IList<ApplicationWindow> windows;
        private readonly VirtualDesktopContext desktops;
        private readonly EventProc callback;
        private readonly IntPtr hook;
        private readonly HashSet<IntPtr> pending = new HashSet<IntPtr>();
        private bool queued;
        private bool disposed;

        internal TaskWindowRecovery(Tasks tasks, TasksService service, VirtualDesktopContext desktops)
        {
            this.service = service;
            this.desktops = desktops;
            windows = tasks.GroupedWindows.SourceCollection as IList<ApplicationWindow>
                ?? throw new InvalidOperationException("Task window source is not mutable.");
            callback = OnUncloaked;
            hook = SetWinEventHook(0x8018, 0x8018, IntPtr.Zero, callback, 0, 0, 2);
            if (hook == IntPtr.Zero) ShellLogger.Error("Task recovery: uncloak hook unavailable.");
            desktops.Changed += DesktopChanged;
            EnumerateCurrentWindows();
        }

        private void DesktopChanged(object sender, EventArgs e) => EnumerateCurrentWindows();

        private void EnumerateCurrentWindows()
        {
            EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd)) pending.Add(hwnd);
                return true;
            }, 0);
            Queue();
        }

        private void OnUncloaked(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (disposed || hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            pending.Add(hwnd);
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
                var handles = pending.ToArray();
                pending.Clear();
                var tracked = new Dictionary<IntPtr, ApplicationWindow>();
                foreach (var window in windows) tracked[window.Handle] = window;
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
                if (!RequiresPanelRefresh(windowAdded, showInTaskbarChanged)) return;
                foreach (var panel in Application.Current.Windows.OfType<Taskbar>())
                    panel.TaskListControl.RefreshWindowVisibility();
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
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            pending.Clear();
        }
    }
}
