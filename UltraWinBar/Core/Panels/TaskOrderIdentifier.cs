using ManagedShell.WindowsTasks;
using System;
using System.Diagnostics;
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

        public static bool IsAlive(string key)
        {
            var parts = key?.Split(':');
            if (parts?.Length != 5 || parts[0] != "window" || parts[1] != "v2" ||
                !uint.TryParse(parts[2], out uint processId) ||
                !long.TryParse(parts[4], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out long handle)) return false;
            var hwnd = new IntPtr(handle);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint actualProcess);
            return NativeMethods.IsWindow(hwnd) && actualProcess == processId;
        }

        public static string GetLegacy(ApplicationWindow window, Tasks tasks)
        {
            string appId = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            if (appId == null || tasks == null)
            {
                return TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.WindowClassAndTitle);
            }

            // Best-effort only — never let a transient enumeration failure here (e.g. a
            // sibling window closing mid-loop) affect drag/assignment/sort callers.
            try
            {
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
            catch (Exception)
            {
                return appId;
            }
        }
    }
}
