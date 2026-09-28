using ManagedShell.Common.Logging;
using ManagedShell.Common.Logging.Observers;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace UltraWinBar.Utilities
{
    class ManagedShellLogger : IDisposable
    {
        // The app is GUI-only; ConsoleLog writes to a console window that doesn't exist unless
        // one was attached (e.g. launched from a terminal), so only attach it in that case.
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        internal static readonly TimeSpan LogRetention = TimeSpan.FromDays(7);

        private string _logPath = "Logs".InLocalAppData();
        private string _logExt = "log";
        private RollingFileLog _fileLog;
        private FilteredLog _filteredFileLog;
        private FilteredLog _filteredConsoleLog;

        public ManagedShellLogger()
        {
            SetupLogging();
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.DebugLogging))
            {
                SetSeverity();
            }
            else if (e.PropertyName == nameof(Settings.DebugLogMasks))
            {
                UpdateMasks();
            }
        }

        private void SetSeverity()
        {
            // Handle null settings instance in case of an error while initializing settings
            ShellLogger.Severity = Settings.Instance?.DebugLogging == true ? LogSeverity.Debug : LogSeverity.Info;
        }

        private void SetupLogging()
        {
            SetSeverity();

            SetupFileLog();

            if (GetConsoleWindow() != IntPtr.Zero)
            {
                _filteredConsoleLog = new FilteredLog(new ConsoleLog(), Settings.Instance.DebugLogMasks);
                ShellLogger.Attach(_filteredConsoleLog, true);
            }
        }

        private void SetupFileLog()
        {
            RollingFileLog.DeleteOldLogFiles(_logPath, _logExt, LogRetention);

            try
            {
                _fileLog = new RollingFileLog(_logPath, _logExt, LogRetention);

                _filteredFileLog = new FilteredLog(_fileLog, Settings.Instance.DebugLogMasks);
                ShellLogger.Attach(_filteredFileLog);
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"Unable to open and attach file logger: {ex.Message}");
            }
        }

        private void UpdateMasks()
        {
            _filteredFileLog?.UpdateMasks(Settings.Instance.DebugLogMasks);
            _filteredConsoleLog?.UpdateMasks(Settings.Instance.DebugLogMasks);
        }

        public void Dispose()
        {
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            if (_filteredFileLog != null)
            {
                ShellLogger.Detach(_filteredFileLog);
            }
            if (_filteredConsoleLog != null)
            {
                ShellLogger.Detach(_filteredConsoleLog);
            }
            _fileLog?.Dispose();
        }
    }

    // Rolls to a new timestamped file once the current one exceeds MaxSizeBytes, so
    // DebugLogging over long uptimes can't grow one log file without bound. Size is
    // tracked by summing message lengths (no per-line FileInfo stat). The roll swaps
    // only the inner FileLog under _lock, so the FilteredLog/ShellLogger attachment
    // above never needs to detach/re-attach.
    internal sealed class RollingFileLog : ILog, IDisposable
    {
        internal const long DefaultMaxSizeBytes = 20 * 1024 * 1024;
        // Once rotation fails (disk full, access denied), don't retry the filesystem on every
        // single log line; back off and try again no more often than this.
        private const int RotationRetryBackoffMs = 30_000;

        private readonly string _logPath;
        private readonly string _logExt;
        private readonly TimeSpan _retention;
        private readonly long _maxSizeBytes;
        private readonly object _lock = new object();
        private FileLog _current;
        private long _currentSize;
        private int _rollSequence;
        private long _nextRotationAttemptTicks;
        private bool _rotationFailureReported;

        // Count of messages that could not be written to any file (current is unavailable).
        // A failed roll alone does not count here: the old file keeps being written to.
        internal long LostLines { get; private set; }

        public RollingFileLog(string logPath, string logExt, TimeSpan retention, long maxSizeBytes = DefaultMaxSizeBytes)
        {
            _logPath = logPath;
            _logExt = logExt;
            _retention = retention;
            _maxSizeBytes = maxSizeBytes;
            _current = OpenNewFile();
        }

        internal static bool ShouldRoll(long currentSize, long maxSizeBytes) => currentSize >= maxSizeBytes;

        internal static string BuildLogFileName(DateTime timestamp, int sequence) =>
            $"{timestamp:yyyy-MM-dd_HHmmssfff}-{sequence:D4}";

        private FileLog OpenNewFile()
        {
            string name = BuildLogFileName(DateTime.Now, _rollSequence++);
            FileLog log = new FileLog(Path.Combine(_logPath, $"{name}.{_logExt}"));
            log.Open();
            _currentSize = 0;
            return log;
        }

        public void Log(object sender, LogEventArgs e)
        {
            lock (_lock)
            {
                try
                {
                    _currentSize += (e.Message?.Length ?? 0) + 32;

                    if (ShouldRoll(_currentSize, _maxSizeBytes) && Environment.TickCount64 >= _nextRotationAttemptTicks)
                    {
                        TryRoll();
                    }

                    if (_current != null)
                    {
                        _current.Log(sender, e);
                    }
                    else
                    {
                        LostLines++;
                    }
                }
                catch (Exception ex)
                {
                    // The logger must never throw into its caller, which can be a native callback.
                    LostLines++;
                    ReportFailureOnce(ex);
                }
            }
        }

        // Opens the next file and swaps to it only on success, so a failed roll keeps writing
        // to the still-open old file instead of losing lines outright.
        private void TryRoll()
        {
            try
            {
                FileLog next = OpenNewFile();
                FileLog old = _current;
                _current = next;
                old.Dispose();
                DeleteOldLogFiles(_logPath, _logExt, _retention);
                _rotationFailureReported = false;
            }
            catch (Exception ex)
            {
                _nextRotationAttemptTicks = Environment.TickCount64 + RotationRetryBackoffMs;
                ReportFailureOnce(ex);
            }
        }

        // Reported once per failure episode (cleared on the next successful roll) so a stuck
        // disk doesn't spam Debug output or the still-open log file on every line.
        private void ReportFailureOnce(Exception ex)
        {
            if (_rotationFailureReported)
            {
                return;
            }

            _rotationFailureReported = true;
            Debug.WriteLine($"[UltraWinBar] Log rotation/write failure, entries may be lost: {ex.Message}");

            try
            {
                _current?.Log(this, new LogEventArgs(LogSeverity.Error, $"Logging error: {ex.Message}", null, DateTime.Now));
            }
            catch
            {
                // Best effort only; the logger must never throw.
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    _current?.Dispose();
                }
                catch
                {
                    // Disposal must not throw either.
                }
            }
        }

        internal static void DeleteOldLogFiles(string logPath, string logExt, TimeSpan retention)
        {
            try
            {
                if (!Directory.Exists(logPath))
                {
                    // Nothing to delete
                    return;
                }

                // look for all of the log files
                DirectoryInfo info = new DirectoryInfo(logPath);
                FileInfo[] files = info.GetFiles($"*.{logExt}", SearchOption.TopDirectoryOnly);

                // delete any files that are older than the retention period
                DateTime now = DateTime.Now;
                foreach (FileInfo file in files)
                {
                    if (now.Subtract(file.LastWriteTime) > retention)
                    {
                        file.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Debug($"Unable to delete old log files: {ex.Message}");
            }
        }
    }
}
