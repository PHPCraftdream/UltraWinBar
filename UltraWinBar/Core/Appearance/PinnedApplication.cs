using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UltraWinBar.Utilities
{
    public sealed class PinnedApplication
    {
        public Guid DesktopId { get; set; }
        [JsonIgnore] public bool OnCurrentDesktop => DesktopId == (VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty);
        public AppBarEdge Edge { get; set; }
        public string Identifier { get; set; }
        public string LaunchTarget { get; set; }
        public string Title { get; set; }
        public string IconPng { get; set; }
        public string PrimaryWindowKey { get; set; }
        [JsonIgnore] public string OrderKey => "pin:" + Identifier;
        [JsonIgnore] public ApplicationWindow.WindowState State => ApplicationWindow.WindowState.Inactive;
        [JsonIgnore] public int ProgressValue => 0;
        [JsonIgnore] public NativeMethods.TBPFLAG ProgressState => NativeMethods.TBPFLAG.TBPF_NOPROGRESS;
        [JsonIgnore] public ImageSource OverlayIcon => null;
        private ImageSource _icon;
        [JsonIgnore] public ImageSource Icon
        {
            get
            {
                if (_icon == null && !string.IsNullOrEmpty(IconPng))
                {
                    try
                    {
                        using var stream = new MemoryStream(Convert.FromBase64String(IconPng));
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        _icon = bitmap;
                    }
                    catch (Exception ex) { ShellLogger.Warning($"Pinned icon: {ex.Message}"); }
                }
                return _icon;
            }
        }

        public static PinnedApplication FromWindow(ApplicationWindow window, AppBarEdge edge)
        {
            string identifier = TaskAssignmentManager.GetIdentifier(window, TaskAssignmentMode.ExecutablePath);
            if (string.IsNullOrEmpty(identifier)) return null;
            string launchTarget = window.IsUWP ? "appx:" + window.AppUserModelID : window.WinFileName;
            if (!window.IsUWP && IsTemporaryTarget(launchTarget))
            {
                launchTarget = SelectLaunchTarget(window.Title);
                if (launchTarget == null) return null;
            }
            var pin = new PinnedApplication
            {
                Edge = edge,
                DesktopId = VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty,
                Identifier = identifier,
                LaunchTarget = launchTarget,
                Title = window.IsUWP ? window.Title : Path.GetFileNameWithoutExtension(window.WinFileName)
            };
            if (window.Icon is BitmapSource bitmap)
            {
                using var stream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                pin.IconPng = Convert.ToBase64String(stream.ToArray());
            }
            return pin;
        }

        internal static bool IsTemporaryTarget(string path) =>
            !string.IsNullOrEmpty(path) && Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        private static string SelectLaunchTarget(string title)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title + " — select a permanent launcher / выберите постоянный файл запуска",
                Filter = "Applications and shortcuts|*.exe;*.lnk|All files|*.*",
                CheckFileExists = true
            };
            while (dialog.ShowDialog() == true)
            {
                if (!IsTemporaryTarget(dialog.FileName)) return dialog.FileName;
                System.Windows.MessageBox.Show("Select a launcher outside the temporary folder.\nВыберите файл запуска вне временной папки.", title);
            }
            return null;
        }

        public void Launch(Tasks tasks)
        {
            try
            {
                Guid desktop = VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty;
                if (DesktopId != desktop) return;
                var target = Settings.Instance.MoveActivatedWindowsToCurrentDesktop
                    ? DesktopShortcutResolver.ReadLaunchTarget(LaunchTarget) : null;
                var matchingWindows = tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>()
                    .Where(w => TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.ExecutablePath) == Identifier).ToList();
                var windows = matchingWindows.Where(w => w.ShowInTaskbar &&
                    VirtualDesktopContext.Instance?.IsOnCurrentDesktop(w.Handle) != false).ToList();
                string folderPath = DesktopWindowLauncher.FolderPath(target);
                bool openLocally = folderPath != null || target != null && DesktopWindowLauncher.SupportsNewWindow(target.TargetPath);
                if (folderPath != null)
                {
                    var folderWindows = DesktopShortcutResolver.ReadFolderWindows(folderPath);
                    windows = windows.Where(w => Array.IndexOf(folderWindows, w.Handle) >= 0).ToList();
                }
                var windowIds = new HashSet<string>(windows.Select(w =>
                    TaskAssignmentManager.GetIdentifier(w, TaskAssignmentMode.WindowClassAndTitle)));
                var assignments = Settings.Instance.TaskbarAssignments.Where(a => a.DesktopId != desktop ||
                    (!(a.Mode == TaskAssignmentMode.ExecutablePath && a.Identifier == Identifier) &&
                     !(a.Mode == TaskAssignmentMode.WindowClassAndTitle && windowIds.Contains(a.Identifier)))).ToList();
                assignments.Add(new TaskbarAssignment { Identifier = Identifier, Edge = Edge, Mode = TaskAssignmentMode.ExecutablePath, DesktopId = desktop });
                Settings.Instance.TaskbarAssignments = assignments;
                var existing = windows.FirstOrDefault(w => w.ShowInTaskbar);
                if (existing != null) existing.BringToFront();
                else if (!openLocally && Settings.Instance.MoveActivatedWindowsToCurrentDesktop &&
                    matchingWindows.FirstOrDefault(w => w.CanAddToTaskbar &&
                        VirtualDesktopContext.Instance?.IsOnCurrentDesktop(w.Handle) == false) is { } remote &&
                    VirtualDesktopContext.Instance?.TryMoveWindowToDesktop(remote.Handle, desktop) == true)
                {
                    ShellLogger.Info($"DesktopActivation: moved pinned window {remote.Handle} to {desktop}.");
                    remote.BringToFront();
                }
                else
                {
                    if (Path.IsPathRooted(LaunchTarget) && !File.Exists(LaunchTarget) && !Directory.Exists(LaunchTarget))
                    {
                        ShellLogger.Warning($"Pinned application target missing: {LaunchTarget}");
                        string replacement = SelectLaunchTarget(Title);
                        if (replacement == null) return;
                        LaunchTarget = replacement;
                        Settings.Instance.PinnedApplications = Settings.Instance.PinnedApplications.ToList();
                        target = Settings.Instance.MoveActivatedWindowsToCurrentDesktop
                            ? DesktopShortcutResolver.ReadLaunchTarget(LaunchTarget) : null;
                        openLocally = DesktopWindowLauncher.FolderPath(target) != null ||
                            target != null && DesktopWindowLauncher.SupportsNewWindow(target.TargetPath);
                    }
                    ShellLogger.Info($"Pinned application launch: {Title}; target={LaunchTarget}; newWindow={openLocally}");
                    if (openLocally) Process.Start(DesktopWindowLauncher.CreateStartInfo(target));
                    else if (!ShellHelper.StartProcess(LaunchTarget))
                        System.Windows.MessageBox.Show("Unable to launch:\n" + LaunchTarget + "\nSee %LOCALAPPDATA%\\UltraWinBar\\Logs.", Title);
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"Pinned application launch failed: {ex}");
                System.Windows.MessageBox.Show(ex.Message, Title);
            }
        }
    }
}
