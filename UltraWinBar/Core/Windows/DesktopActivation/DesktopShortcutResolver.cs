using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UltraWinBar.Utilities
{
    internal sealed class DesktopShortcutSelection
    {
        public string ShortcutPath { get; }
        public string TargetPath { get; }
        public string Arguments { get; }
        public string WorkingDirectory { get; }
        public bool RunAsAdministrator { get; }

        public DesktopShortcutSelection(string shortcutPath, string targetPath, string arguments = "",
            string workingDirectory = "", bool runAsAdministrator = false)
        {
            ShortcutPath = shortcutPath;
            TargetPath = targetPath;
            Arguments = arguments ?? "";
            WorkingDirectory = workingDirectory ?? "";
            RunAsAdministrator = runAsAdministrator;
        }
    }

    internal static class DesktopShortcutResolver
    {
        private static readonly ParameterModifier[] FindWindowModifiers = CreateFindWindowModifiers();

        private static ParameterModifier[] CreateFindWindowModifiers()
        {
            var modifier = new ParameterModifier(5);
            modifier[0] = modifier[1] = modifier[3] = true;
            return new[] { modifier };
        }

        // CLR dispatch avoids the C# binder's duplicate-GUID Shell typelib scan.
        private static object Get(object target, string name) =>
            target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

        private static object Call(object target, string name, object[] arguments = null,
            ParameterModifier[] modifiers = null) =>
            target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target,
                arguments, modifiers, CultureInfo.InvariantCulture, null);

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        internal static DesktopShortcutSelection ReadLaunchTarget(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                return new DesktopShortcutSelection(path, path);
            if (!File.Exists(path)) return new DesktopShortcutSelection(path, null);
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                shortcut = Call(shell, "CreateShortcut", new object[] { path });
                bool runAs = false;
                using (var stream = File.OpenRead(path))
                {
                    Span<byte> flags = stackalloc byte[4];
                    stream.Position = 20;
                    if (stream.Read(flags) == flags.Length) runAs = (BitConverter.ToUInt32(flags) & 0x2000) != 0;
                }
                return new DesktopShortcutSelection(path, (string)Get(shortcut, "TargetPath"),
                    (string)Get(shortcut, "Arguments"), (string)Get(shortcut, "WorkingDirectory"), runAs);
            }
            finally
            {
                Release(shortcut);
                Release(shell);
            }
        }

        internal static void StartInDesktopShell(ProcessStartInfo start)
        {
            object shell = null, windows = null, desktop = null, document = null, application = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                windows = Call(shell, "Windows");
                object[] findArguments = { 0, null, 8, 0, 1 };
                desktop = Call(windows, "FindWindowSW", findArguments, FindWindowModifiers);
                document = Get(desktop, "Document");
                application = Get(document, "Application");
                ManagedShell.Interop.NativeMethods.GetWindowThreadProcessId(
                    new IntPtr(Convert.ToInt64(findArguments[3], CultureInfo.InvariantCulture)), out uint processId);
                if (processId != 0) ManagedShell.Interop.NativeMethods.AllowSetForegroundWindow(processId);
                // The desktop shell owns activation, not the background mouse-hook client.
                Call(application, "ShellExecute", new object[]
                {
                    start.FileName, start.Arguments, start.WorkingDirectory, start.Verb, 1
                });
            }
            finally
            {
                Release(application);
                Release(document);
                Release(desktop);
                Release(windows);
                Release(shell);
            }
        }

        public static DesktopShortcutSelection ReadSelectedShortcut()
        {
            var items = ReadSelectedItems();
            return items.Length == 1 ? items[0] : null;
        }

        internal static DesktopShortcutSelection[] ReadSelectedItems()
        {
            object shell = null, windows = null, desktop = null, document = null, selected = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                windows = Call(shell, "Windows");
                object[] findArguments = { 0, null, 8, 0, 1 };
                desktop = Call(windows, "FindWindowSW", findArguments, FindWindowModifiers);
                document = Get(desktop, "Document");
                selected = Call(document, "SelectedItems");
                int count = (int)Get(selected, "Count");
                var result = new DesktopShortcutSelection[count];
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = Call(selected, "Item", new object[] { i });
                        string path = (string)Get(item, "Path");
                        result[i] = ReadLaunchTarget(path);
                    }
                    finally { Release(item); }
                }
                return result;
            }
            finally
            {
                Release(selected);
                Release(document);
                Release(desktop);
                Release(windows);
                Release(shell);
            }
        }

        internal static IntPtr[] ReadFolderWindows(string path)
        {
            var result = new List<IntPtr>();
            object shell = null, windows = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                windows = Call(shell, "Windows");
                int count = (int)Get(windows, "Count");
                for (int i = 0; i < count; i++)
                {
                    object window = null, document = null, folder = null, item = null;
                    try
                    {
                        window = Call(windows, "Item", new object[] { i });
                        if (window == null) continue;
                        document = Get(window, "Document");
                        if (document == null) continue;
                        folder = Get(document, "Folder");
                        if (folder == null) continue;
                        item = Get(folder, "Self");
                        if (item == null) continue;
                        string openPath = (string)Get(item, "Path");
                        if (openPath != null && string.Equals(openPath.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            result.Add(new IntPtr(Convert.ToInt64(Get(window, "HWND"), CultureInfo.InvariantCulture)));
                    }
                    catch (TargetInvocationException error) when (error.InnerException is COMException) { }
                    catch (COMException) { }
                    catch (MissingMemberException) { }
                    finally
                    {
                        Release(item);
                        Release(folder);
                        Release(document);
                        Release(window);
                    }
                }
                return result.ToArray();
            }
            finally
            {
                Release(windows);
                Release(shell);
            }
        }
    }
}
