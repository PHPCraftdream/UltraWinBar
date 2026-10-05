using ManagedShell.Common.Logging;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;

namespace UltraWinBar.Utilities
{
    // Opt-in journal of taskbar windows opening/closing: Logs\windows-yyyy-MM-dd.log while
    // %LOCALAPPDATA%\UltraWinBar\window-log.enabled exists (checked per event, so toggling is live).
    internal sealed class WindowActivityLog : IDisposable
    {
        internal const string FlagFileName = "window-log.enabled";
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly INotifyCollectionChanged windows;
        private readonly string logDirectory;
        private readonly Func<bool> isEnabled;
        private bool disposed;

        internal WindowActivityLog(Tasks tasks)
            : this(tasks?.GroupedWindows?.SourceCollection as INotifyCollectionChanged, "Logs".InLocalAppData(),
                () => File.Exists(FlagFileName.InLocalAppData()))
        {
        }

        internal WindowActivityLog(INotifyCollectionChanged windows, string logDirectory, Func<bool> isEnabled)
        {
            this.windows = windows;
            this.logDirectory = logDirectory;
            this.isEnabled = isEnabled;
            if (windows != null) windows.CollectionChanged += WindowsChanged;
        }

        internal static string FileNameFor(DateTime at) => $"windows-{at:yyyy-MM-dd}.log";

        internal static string FormatLine(DateTime at, string kind, IntPtr hwnd, string binary, string title) =>
            $"{at:yyyy-MM-dd HH:mm:ss.fff}\t{kind}\t0x{hwnd.ToInt64():X}\t{Clean(binary)}\t{Clean(title)}";

        private static string Clean(string value) =>
            string.IsNullOrEmpty(value) ? "-" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private void WindowsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (disposed || e.Action == NotifyCollectionChangedAction.Reset || e.Action == NotifyCollectionChangedAction.Move) return;
            if (!isEnabled()) return;
            DateTime now = DateTime.Now;
            foreach (var window in e.OldItems?.OfType<ApplicationWindow>() ?? Enumerable.Empty<ApplicationWindow>())
                Record(now, "closed", window.Handle, window.WinFileName, window.Title);
            foreach (var window in e.NewItems?.OfType<ApplicationWindow>() ?? Enumerable.Empty<ApplicationWindow>())
                Record(now, "opened", window.Handle, window.WinFileName, window.Title);
        }

        internal void Record(DateTime at, string kind, IntPtr hwnd, string binary, string title)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(Path.Combine(logDirectory, FileNameFor(at)),
                    FormatLine(at, kind, hwnd, binary, title) + Environment.NewLine, Utf8NoBom);
            }
            catch (Exception ex)
            {
                ShellLogger.Debug($"WindowActivityLog: write failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (windows != null) windows.CollectionChanged -= WindowsChanged;
        }
    }
}
