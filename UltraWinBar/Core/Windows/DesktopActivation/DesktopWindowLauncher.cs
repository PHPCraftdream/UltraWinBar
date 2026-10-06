using System;
using System.Diagnostics;
using System.IO;

namespace UltraWinBar.Utilities
{
    internal static class DesktopWindowLauncher
    {
        private enum LaunchKind { Default, Chromium, Firefox, NotepadPlusPlus, Explorer }

        private static LaunchKind Kind(string executable)
        {
            ReadOnlySpan<char> name = Path.GetFileName(executable.AsSpan());
            static bool Is(ReadOnlySpan<char> value, string expected) =>
                value.Equals(expected.AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (Is(name, "chrome.exe") || Is(name, "chromium.exe") || Is(name, "msedge.exe") ||
                Is(name, "browser.exe") || Is(name, "brave.exe") || Is(name, "vivaldi.exe") || Is(name, "opera.exe"))
                return LaunchKind.Chromium;
            if (Is(name, "firefox.exe") || Is(name, "waterfox.exe") || Is(name, "librewolf.exe"))
                return LaunchKind.Firefox;
            if (Is(name, "notepad++.exe")) return LaunchKind.NotepadPlusPlus;
            if (Is(name, "explorer.exe")) return LaunchKind.Explorer;
            return LaunchKind.Default;
        }

        internal static bool SupportsNewWindow(string executable) => Kind(executable) != LaunchKind.Default;

        internal static string NewWindowArguments(string executable, string arguments)
        {
            arguments ??= "";
            switch (Kind(executable))
            {
                case LaunchKind.Chromium:
                    return "--new-window" + (arguments.Length == 0 ? "" : " " + arguments);
                case LaunchKind.Firefox:
                    if (arguments.Length == 0) return "-new-window about:blank";
                    return arguments.TrimStart().StartsWith("-", StringComparison.Ordinal)
                        ? arguments + " -new-window about:blank" : "-new-window " + arguments;
                case LaunchKind.NotepadPlusPlus:
                    return "-multiInst -nosession" + (arguments.Length == 0 ? "" : " " + arguments);
                case LaunchKind.Explorer:
                    return "/n," + (arguments.Length == 0 ? "shell:MyComputerFolder" : arguments);
                default:
                    return null;
            }
        }

        internal static string FolderPath(DesktopShortcutSelection target)
        {
            if (target == null || string.IsNullOrEmpty(target.TargetPath)) return null;
            if (Directory.Exists(target.TargetPath) || target.TargetPath.StartsWith("::", StringComparison.Ordinal))
                return target.TargetPath;
            if (Kind(target.TargetPath) != LaunchKind.Explorer)
                return null;
            string path = target.Arguments.Trim();
            while (path.StartsWith("/n,", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/e,", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(3).TrimStart();
            path = path.Trim('"');
            return Directory.Exists(path) || path.StartsWith("::", StringComparison.Ordinal) ? path : null;
        }

        internal static ProcessStartInfo CreateStartInfo(DesktopShortcutSelection target)
        {
            if (target == null || string.IsNullOrEmpty(target.TargetPath)) return null;
            string executable = target.TargetPath;
            string arguments;
            if (Directory.Exists(executable) || executable.StartsWith("::", StringComparison.Ordinal))
            {
                arguments = "/n,\"" + executable + (executable.EndsWith("\\", StringComparison.Ordinal) ? "\\" : "") + "\"";
                executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            }
            else
            {
                arguments = NewWindowArguments(executable, target.Arguments);
                if (arguments == null) return null;
            }
            return new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = target.WorkingDirectory,
                UseShellExecute = true,
                Verb = target.RunAsAdministrator ? "runas" : ""
            };
        }
    }
}
