using ManagedShell.Common.Logging;
using ManagedShell.Common.Logging.Observers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

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
        private readonly System.Windows.Threading.DispatcherTimer _debugFlagTimer;
        private readonly string _debugFlagPath = "debug-logging.enabled".InLocalAppData();
        private bool _debugFlagEnabled;

        public ManagedShellLogger()
        {
            SetupLogging();
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
            _debugFlagTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _debugFlagTimer.Tick += DebugFlagTimer_Tick;
            _debugFlagTimer.Start();
        }

        // K19 health line: lines lost outright, plus lines dropped because the write queue overflowed.
        internal long LostLogLines => _fileLog?.LostLines ?? 0;
        internal long DroppedQueuedLogLines => _fileLog?.DroppedQueuedLines ?? 0;

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

        private void DebugFlagTimer_Tick(object sender, EventArgs e)
        {
            bool enabled = File.Exists(_debugFlagPath);
            if (enabled == _debugFlagEnabled) return;
            SetSeverity();
            ShellLogger.Info($"Debug logging flag: {(enabled ? "enabled" : "disabled")}; severity={ShellLogger.Severity}");
        }

        private void SetSeverity()
        {
            // Handle null settings instance in case of an error while initializing settings
            _debugFlagEnabled = File.Exists(_debugFlagPath);
            ShellLogger.Severity = Settings.Instance?.DebugLogging == true || _debugFlagEnabled ? LogSeverity.Debug : LogSeverity.Info;
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
            _debugFlagTimer.Stop();
            _debugFlagTimer.Tick -= DebugFlagTimer_Tick;
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
    //
    // Writing is off the caller's thread: Debug/Info/Warning are queued and written+flushed in
    // batches by one background writer thread. Error/Fatal (and the one-time overflow report)
    // write and flush synchronously so they, and anything already queued ahead of them, are on
    // disk before the call returns - this is what keeps the last lines before a crash intact,
    // since App.xaml.cs logs Error before every FailFast.
    internal sealed class RollingFileLog : ILog, IDisposable
    {
        internal const long DefaultMaxSizeBytes = 20 * 1024 * 1024;
        internal const int DefaultMaxQueueDepth = 2000;
        // Once rotation fails (disk full, access denied), don't retry the filesystem on every
        // single log line; back off and try again no more often than this.
        private const int RotationRetryBackoffMs = 30_000;
        // Bounded wait for the writer thread to drain on Dispose; never block shutdown forever.
        private const int DrainOnDisposeMs = 2000;

        private readonly string _logPath;
        private readonly string _logExt;
        private readonly TimeSpan _retention;
        private readonly long _maxSizeBytes;
        private readonly int _maxQueueDepth;
        private readonly object _lock = new object();
        private FileLog _current;
        private long _currentSize;
        private int _rollSequence;
        private long _nextRotationAttemptTicks;
        private bool _rotationFailureReported;

        private readonly object _queueLock = new object();
        private readonly Queue<LogEventArgs> _pending = new Queue<LogEventArgs>();
        private readonly SemaphoreSlim _pendingSignal = new SemaphoreSlim(0, int.MaxValue);
        private readonly ManualResetEventSlim _idle = new ManualResetEventSlim(true);
        // Test-only hook: held open in production. A test resets it to pause the writer thread
        // so it can deterministically overflow the queue without racing a live consumer.
        private readonly ManualResetEventSlim _writerGate = new ManualResetEventSlim(true);
        private readonly Thread _writerThread;
        private volatile bool _shuttingDown;
        private long _droppedQueuedLines;

        // Count of messages that could not be written to any file (current is unavailable).
        // A failed roll alone does not count here: the old file keeps being written to.
        internal long LostLines { get; private set; }

        // Count of messages dropped because the write queue was full (K19 health line).
        internal long DroppedQueuedLines => Interlocked.Read(ref _droppedQueuedLines);

        public RollingFileLog(string logPath, string logExt, TimeSpan retention, long maxSizeBytes = DefaultMaxSizeBytes, int maxQueueDepth = DefaultMaxQueueDepth)
        {
            _logPath = logPath;
            _logExt = logExt;
            _retention = retention;
            _maxSizeBytes = maxSizeBytes;
            _maxQueueDepth = maxQueueDepth;
            _current = OpenNewFile();
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "UltraWinBar-LogWriter" };
            _writerThread.Start();
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
            if (e.Severity == LogSeverity.Error || e.Severity == LogSeverity.Fatal)
            {
                // Crash paths log Error/Fatal right before FailFast: write immediately, and drain
                // anything already queued first so the file stays in chronological order.
                WriteDirect(e);
                return;
            }

            long freedDrops = 0;
            lock (_queueLock)
            {
                if (_pending.Count >= _maxQueueDepth)
                {
                    _droppedQueuedLines++;
                    return;
                }

                if (_droppedQueuedLines > 0)
                {
                    freedDrops = _droppedQueuedLines;
                    _droppedQueuedLines = 0;
                }

                _pending.Enqueue(e);
                _idle.Reset();
            }
            _pendingSignal.Release();

            if (freedDrops > 0)
            {
                WriteDirect(new LogEventArgs(LogSeverity.Warning, $"Log queue overflow: dropped {freedDrops} line(s).", null, DateTime.Now));
            }
        }

        private void WriterLoop()
        {
            while (true)
            {
                _pendingSignal.Wait();
                _writerGate.Wait();
                bool shuttingDown = _shuttingDown;
                DrainOnePass();
                if (shuttingDown)
                {
                    return;
                }
            }
        }

        // Writes and flushes one batch: everything currently queued, one flush at the end.
        private void DrainOnePass()
        {
            lock (_lock)
            {
                DrainQueueLocked();
                FlushCurrent();
            }

            lock (_queueLock)
            {
                if (_pending.Count == 0)
                {
                    _idle.Set();
                }
            }
        }

        // Writes straight to disk, ahead of the queue: used for Error/Fatal and the one-time
        // overflow report, so they land on disk before the call returns.
        private void WriteDirect(LogEventArgs e)
        {
            lock (_lock)
            {
                DrainQueueLocked();
                WriteOneLocked(e);
                FlushCurrent();
            }

            lock (_queueLock)
            {
                if (_pending.Count == 0)
                {
                    _idle.Set();
                }
            }
        }

        // Must be called with _lock held.
        private void DrainQueueLocked()
        {
            List<LogEventArgs> batch = null;
            lock (_queueLock)
            {
                if (_pending.Count > 0)
                {
                    batch = new List<LogEventArgs>(_pending);
                    _pending.Clear();
                }
            }

            if (batch == null)
            {
                return;
            }

            foreach (LogEventArgs item in batch)
            {
                WriteOneLocked(item);
            }
        }

        // Must be called with _lock held. Rolls if needed and writes to the current file, but
        // does not flush - the caller (batch or immediate write) decides when to flush.
        private void WriteOneLocked(LogEventArgs e)
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
                    _current.Log(this, e);
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

        // Must be called with _lock held.
        private void FlushCurrent()
        {
            try
            {
                _current?.Flush();
            }
            catch (Exception ex)
            {
                ReportFailureOnce(ex);
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
                _current?.Flush();
            }
            catch
            {
                // Best effort only; the logger must never throw.
            }
        }

        // Test hook: blocks until the queue has been fully drained, or the timeout elapses.
        internal bool WaitForIdle(int timeoutMs) => _idle.Wait(timeoutMs);

        public void Dispose()
        {
            _shuttingDown = true;
            try
            {
                _pendingSignal.Release();
            }
            catch
            {
                // Disposal must not throw.
            }

            try
            {
                _writerThread.Join(DrainOnDisposeMs);
            }
            catch
            {
                // Disposal must not throw.
            }

            // TryEnter with its own short bound: if the writer thread is still stuck inside
            // _lock past the join above (e.g. a hung disk write), Dispose must still return
            // within a bounded time rather than wait on that lock forever.
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_lock, 250, ref lockTaken);
                if (lockTaken)
                {
                    // In case the writer thread didn't finish draining within the bound above.
                    DrainQueueLocked();
                    _current?.Dispose();
                }
            }
            catch
            {
                // Disposal must not throw either.
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(_lock);
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
