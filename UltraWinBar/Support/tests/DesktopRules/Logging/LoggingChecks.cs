using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using UltraWinBar.Utilities;

// Covers R9-E (round-8 review, N9/K17 priority-1 slice): RollingFileLog must never throw out of
// Log(), even when rotation or the underlying write is impossible, and ShellLogger's orphan
// buffer must be bounded and thread-safe with per-observer exception isolation.
internal static class LoggingChecks
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunRollingFileLogNoFallbackCheck();
        RunRollingFileLogRotationFailureCheck();
        RunShellLoggerOrphanBufferCheck();
        RunShellLoggerObserverIsolationCheck();
    }

    // No file at all to write to (e.g. the initial open never succeeded): every line must be
    // counted as lost, never thrown.
    private static void RunRollingFileLogNoFallbackCheck()
    {
        Type rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(logDirectory);
        try
        {
            object instance = Activator.CreateInstance(rollingLogType, new object[] { logDirectory, "log", TimeSpan.FromDays(7), long.MaxValue });
            var log = (ILog)instance;
            FieldInfo currentField = rollingLogType.GetField("_current", NonPublicInstance);
            PropertyInfo lostLinesProperty = rollingLogType.GetProperty("LostLines", NonPublicInstance);

            // Close the file the constructor opened before dropping the reference, so nulling
            // _current below (simulating "no file available") doesn't leak an open handle.
            ((IDisposable)currentField.GetValue(instance)).Dispose();
            currentField.SetValue(instance, null);

            for (int i = 0; i < 5; i++)
                log.Log(null, new LogEventArgs(LogSeverity.Info, $"line {i}", null, DateTime.Now));

            long lostLines = (long)lostLinesProperty.GetValue(instance);
            if (lostLines != 5)
                throw new Exception($"Expected 5 lost lines when the logger has no file to write to, got {lostLines}.");

            ((IDisposable)instance).Dispose();
        }
        finally
        {
            System.IO.Directory.Delete(logDirectory, true);
        }
        Console.WriteLine("PASS: RollingFileLog never throws and counts lost lines when no file is available to write to.");
    }

    // Rotation target is unwritable (a file sits where the roll needs to create a directory) -
    // simulates disk-full/access-denied on the new file. The logger must not throw, must keep
    // writing to the still-open old file, and must back off retrying rotation on every line.
    private static void RunRollingFileLogRotationFailureCheck()
    {
        Type rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-" + Guid.NewGuid().ToString("N"));
        string blockedPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-blocked-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(logDirectory);
        try
        {
            // A regular file where the rotation target expects a directory: CreateDirectory fails
            // the same way it would on disk-full or access-denied, deterministically.
            System.IO.File.WriteAllText(blockedPath, string.Empty);

            // Tiny cap so the very first line already needs to roll.
            object instance = Activator.CreateInstance(rollingLogType, new object[] { logDirectory, "log", TimeSpan.FromDays(7), 1L });
            var log = (ILog)instance;
            FieldInfo logPathField = rollingLogType.GetField("_logPath", NonPublicInstance);
            FieldInfo rollSequenceField = rollingLogType.GetField("_rollSequence", NonPublicInstance);
            FieldInfo nextAttemptField = rollingLogType.GetField("_nextRotationAttemptTicks", NonPublicInstance);
            PropertyInfo lostLinesProperty = rollingLogType.GetProperty("LostLines", NonPublicInstance);

            logPathField.SetValue(instance, blockedPath);

            log.Log(null, new LogEventArgs(LogSeverity.Info, "line 0", null, DateTime.Now));
            int sequenceAfterFirstFailure = (int)rollSequenceField.GetValue(instance);
            long backoffUntil = (long)nextAttemptField.GetValue(instance);
            if (backoffUntil <= Environment.TickCount64)
                throw new Exception("A failed roll must back off before the next retry.");

            log.Log(null, new LogEventArgs(LogSeverity.Info, "line 1", null, DateTime.Now));
            int sequenceAfterSecondCall = (int)rollSequenceField.GetValue(instance);
            if (sequenceAfterSecondCall != sequenceAfterFirstFailure)
                throw new Exception("A second log call within the backoff window must not retry rotation.");

            long lostLines = (long)lostLinesProperty.GetValue(instance);
            if (lostLines != 0)
                throw new Exception($"A failed roll must keep writing to the still-open old file, not count lines as lost; got {lostLines}.");

            ((IDisposable)instance).Dispose();

            string[] files = System.IO.Directory.GetFiles(logDirectory, "*.log");
            if (files.Length != 1)
                throw new Exception($"Expected the original file to be the only file in the log directory, found {files.Length}.");
            string contents = System.IO.File.ReadAllText(files[0]);
            if (!contents.Contains("line 0") || !contents.Contains("line 1"))
                throw new Exception("Lines logged while rotation is failing must still land in the old file.");
        }
        finally
        {
            System.IO.Directory.Delete(logDirectory, true);
            if (System.IO.File.Exists(blockedPath)) System.IO.File.Delete(blockedPath);
        }
        Console.WriteLine("PASS: RollingFileLog never throws when rotation fails, keeps writing to the old file, and backs off retries.");
    }

    // No observer attached: events must accumulate in a bounded ring buffer, and concurrent
    // producers from several threads must neither throw nor corrupt it.
    private static void RunShellLoggerOrphanBufferCheck()
    {
        FieldInfo logField = typeof(ShellLogger).GetField("Log", NonPublicStatic);
        if (logField.GetValue(null) != null)
            throw new Exception("Test precondition failed: ShellLogger already has an observer attached.");

        FieldInfo orphanedField = typeof(ShellLogger).GetField("_orphanedEvents", NonPublicStatic);
        FieldInfo droppedField = typeof(ShellLogger).GetField("_droppedOrphanedEvents", NonPublicStatic);
        FieldInfo lockField = typeof(ShellLogger).GetField("_orphanLock", NonPublicStatic);
        object orphanLock = lockField.GetValue(null);

        // Start from a clean slate regardless of what earlier checks in this process logged.
        lock (orphanLock)
        {
            ((Queue<LogEventArgs>)orphanedField.GetValue(null)).Clear();
            droppedField.SetValue(null, 0L);
        }

        const int threadCount = 8;
        const int perThread = 200;
        const int totalAdds = threadCount * perThread;
        var barrier = new Barrier(threadCount);
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            tasks[t] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < perThread; i++)
                    ShellLogger.Debug($"orphan-buffer-test thread {threadIndex} line {i}");
            });
        }
        Task.WaitAll(tasks);

        int bufferedCount;
        long dropped;
        lock (orphanLock)
        {
            bufferedCount = ((Queue<LogEventArgs>)orphanedField.GetValue(null)).Count;
            dropped = (long)droppedField.GetValue(null);
        }

        if (bufferedCount != 500)
            throw new Exception($"Orphan buffer must be bounded to 500 entries, has {bufferedCount}.");
        if (dropped != totalAdds - 500)
            throw new Exception($"Expected {totalAdds - 500} dropped orphaned events, got {dropped}.");

        lock (orphanLock)
        {
            ((Queue<LogEventArgs>)orphanedField.GetValue(null)).Clear();
            droppedField.SetValue(null, 0L);
        }

        Console.WriteLine("PASS: ShellLogger's orphan buffer is bounded to 500 entries and concurrent adds from multiple threads neither throw nor corrupt it.");
    }

    // One observer throwing must not stop the others from being notified, and must not escape
    // ShellLogger into the caller (which can be a native callback).
    private static void RunShellLoggerObserverIsolationCheck()
    {
        var received = new List<string>();
        var throwingObserver = new RecordingLog(_ => throw new InvalidOperationException("boom"));
        var recordingObserver = new RecordingLog(e => received.Add(e.Message));

        ShellLogger.Attach(throwingObserver);
        ShellLogger.Attach(recordingObserver);
        try
        {
            ShellLogger.Info("observer isolation probe");
        }
        catch (Exception ex)
        {
            throw new Exception("ShellLogger must not let an observer's exception escape.", ex);
        }
        finally
        {
            ShellLogger.Detach(throwingObserver);
            ShellLogger.Detach(recordingObserver);
        }

        if (!received.Contains("observer isolation probe"))
            throw new Exception("A throwing observer must not prevent other observers from being notified.");

        Console.WriteLine("PASS: ShellLogger isolates observer exceptions; one throwing observer doesn't stop the others or escape the caller.");
    }

    private sealed class RecordingLog : ILog
    {
        private readonly Action<LogEventArgs> _onLog;
        public RecordingLog(Action<LogEventArgs> onLog) => _onLog = onLog;
        public void Log(object sender, LogEventArgs e) => _onLog(e);
    }
}
