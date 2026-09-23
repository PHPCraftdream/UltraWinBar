using System;
using System.IO;

namespace UltraWinBar.Utilities
{
    internal sealed class DesktopShortcutSelection
    {
        public string ShortcutPath { get; }
        public string TargetPath { get; }

        public DesktopShortcutSelection(string shortcutPath, string targetPath)
        {
            ShortcutPath = shortcutPath;
            TargetPath = targetPath;
        }
    }

    internal static class DesktopShortcutResolver
    {
        public static DesktopShortcutSelection ReadSelectedShortcut()
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic windows = shell.Windows();
                object location = 0, root = null;
                int desktopHwnd = 0;
                dynamic desktop = windows.FindWindowSW(ref location, ref root, 8, out desktopHwnd, 1);
                dynamic selected = desktop.Document.SelectedItems();
                if ((int)selected.Count != 1) return null;

                dynamic item = selected.Item(0);
                string shortcutPath = (string)item.Path;
                if (!string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(shortcutPath)) return null;

                dynamic scriptShell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic shortcut = scriptShell.CreateShortcut(shortcutPath);
                string targetPath = (string)shortcut.TargetPath;
                if (!string.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(targetPath)) return null;

                return new DesktopShortcutSelection(shortcutPath, targetPath);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
