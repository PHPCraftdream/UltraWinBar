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
                foreach (var hwnd in handles)
                {
                    if (!IsWindow(hwnd)) continue;
                    var existing = windows.FirstOrDefault(window => window.Handle == hwnd);
                    if (existing != null) { existing.SetShowInTaskbar(); continue; }
                    var candidate = new ApplicationWindow(service, hwnd);
                    if (candidate.CanAddToTaskbar)
                    {
                        candidate.SetShowInTaskbar();
                        windows.Add(candidate);
                        ShellLogger.Info($"Task recovery: restored window {hwnd}");
                    }
                    else candidate.Dispose();
                }
                foreach (var panel in Application.Current.Windows.OfType<Taskbar>())
                    panel.TaskListControl.RefreshWindowVisibility();
            }), DispatcherPriority.Background);
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
