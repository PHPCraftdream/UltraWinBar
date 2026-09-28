using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using UltraWinBar.Utilities;

// Covers R9-E (round-8 review, N9/K17 priority-1 slice): RollingFileLog must never throw out of
// Log(), even when rotation or the underlying write is impossible, and ShellLogger's orphan
// buffer must be bounded and thread-safe with per-observer exception isolation.
// Extended for R9-K (round-8 review, N9/K17): background writing (queued Debug/Info, immediate
// Error/Fatal, bounded overflow with a one-time report, bounded Dispose drain) and lazy
// interpolated-string formatting for Debug.
internal static class LoggingChecks
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunRollingFileLogNoFallbackCheck();
        RunRollingFileLogRotationFailureCheck();
        RunRollingFileLogOrderCheck();
        RunRollingFileLogErrorImmediateCheck();
        RunRollingFileLogOverflowCheck();
        RunRollingFileLogDisposeDrainCheck();
        RunShellLoggerOrphanBufferCheck();
        RunShellLoggerObserverIsolationCheck();
        RunShellLoggerLazyDebugFormattingCheck();
    }

    private static object CreateRollingFileLog(Type rollingLogType, string logDirectory, long maxSizeBytes, int maxQueueDepth) =>
        Activator.CreateInstance(rollingLogType, new object[] { logDirectory, "log", TimeSpan.FromDays(7), maxSizeBytes, maxQueueDepth });

    private static bool WaitForIdle(Type rollingLogType, object instance, int timeoutMs) =>
        (bool)rollingLogType.GetMethod("WaitForIdle", NonPublicInstance).Invoke(instance, new object[] { timeoutMs });

    // No file at all to write to (e.g. the initial open never succeeded): every line must be
    // counted as lost, never thrown.
    private static void RunRollingFileLogNoFallbackCheck()
    {
        Type rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(logDirectory);
        try
        {
            object instance = CreateRollingFileLog(rollingLogType, logDirectory, long.MaxValue, 100);
            var log = (ILog)instance;
            FieldInfo currentField = rollingLogType.GetField("_current", NonPublicInstance);
            PropertyInfo lostLinesProperty = rollingLogType.GetProperty("LostLines", NonPublicInstance);

            // Close the file the constructor opened before dropping the reference, so nulling
            // _current below (simulating "no file available") doesn't leak an open handle. Safe
            // to touch _current without the writer's lock: nothing has been queued yet, so the
            // writer thread is still parked waiting for the first item.
            ((IDisposable)currentField.GetValue(instance)).Dispose();
            currentField.SetValue(instance, null);

            for (int i = 0; i < 5; i++)
                log.Log(null, new LogEventArgs(LogSeverity.Info, $"line {i}", null, DateTime.Now));

            // Info lines are written by the background writer thread; wait for it to drain.
            if (!WaitForIdle(rollingLogType, instance, 2000))
                throw new Exception("Writer thread did not drain within the bound.");

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
            object instance = CreateRollingFileLog(rollingLogType, logDirectory, 1L, 100);
            var log = (ILog)instance;
            FieldInfo logPathField = rollingLogType.GetField("_logPath", NonPublicInstance);
            FieldInfo rollSequenceField = rollingLogType.GetField("_rollSequence", NonPublicInstance);
            FieldInfo nextAttemptField = rollingLogType.GetField("_nextRotationAttemptTicks", NonPublicInstance);
            PropertyInfo lostLinesProperty = rollingLogType.GetProperty("LostLines", NonPublicInstance);

            logPathField.SetValue(instance, blockedPath);

            // Info lines are written by the background writer thread; wait for each to drain
            // before inspecting rotation state, so the checks below aren't racing the writer.
            log.Log(null, new LogEventArgs(LogSeverity.Info, "line 0", null, DateTime.Now));
            if (!WaitForIdle(rollingLogType, instance, 2000))
                throw new Exception("Writer thread did not drain within the bound.");
            int sequenceAfterFirstFailure = (int)rollSequenceField.GetValue(instance);
            long backoffUntil = (long)nextAttemptField.GetValue(instance);
            if (backoffUntil <= Environment.TickCount64)
                throw new Exception("A failed roll must back off before the next retry.");

            log.Log(null, new LogEventArgs(LogSeverity.Info, "line 1", null, DateTime.Now));
            if (!WaitForIdle(rollingLogType, instance, 2000))
                throw new Exception("Writer thread did not drain within the bound.");
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

    // Debug/Info are queued and written by one background writer thread: order must be preserved
    // end to end even though the writes happen off the calling thread.
    private static void RunRollingFileLogOrderCheck()
    {
        Type rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(logDirectory);
        try
        {
            const int count = 300;
            object instance = CreateRollingFileLog(rollingLogType, logDirectory, long.MaxValue, count + 10);
            var log = (ILog)instance;

            for (int i = 0; i < count; i++)
                log.Log(null, new LogEventArgs(LogSeverity.Info, $"order-{i:D4}", null, DateTime.Now));

            if (!WaitForIdle(rollingLogType, instance, 2000))
                throw new Exception("Writer thread did not drain within the bound.");

            ((IDisposable)instance).Dispose();

            string[] files = System.IO.Directory.GetFiles(logDirectory, "*.log");
            string[] orderLines = files.SelectMany(System.IO.File.ReadAllLines).Where(line => line.Contains("order-")).ToArray();
            if (orderLines.Length != count)
                throw new Exception($"Expected {count} queued lines on disk, found {orderLines.Length}.");
            for (int i = 0; i < count; i++)
                if (!orderLines[i].Contains($"order-{i:D4}"))
                    throw new Exception($"Writer thread did not preserve enqueue order at index {i}: '{orderLines[i]}'.");
        }
        finally
        {
            System.IO.Directory.Delete(logDirectory, true);
        }
        Console.WriteLine("PASS: the background writer thread drains the queue and preserves enqueue order.");
    }

    // Error/Fatal must be on disk immediately after Log() returns, with no wait for the
    // background writer - this is what keeps a crash's last log line from being lost.
    private static void RunRollingFileLogErrorImmediateCheck()
    {
        Type rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(logDirectory);
        try
        {
            object instance = CreateRollingFileLog(rollingLogType, logDirectory, long.MaxValue, 100);
            var log = (ILog)instance;
            FieldInfo pendingField = rollingLogType.GetField("_pending", NonPublicInstance);

            log.Log(null, new LogEventArgs(LogSeverity.Error, "immediate-error-probe", null, DateTime.Now));

            // Deliberately no wait: Error must take the direct write-and-flush path, not the
            // queue the background writer drains later - nothing should be left queued.
            var pending = (Queue<LogEventArgs>)pendingField.GetValue(instance);
            if (pending.Count != 0)
                throw new Exception("Error must write directly, not go through the background writer's queue.");

            ((IDisposable)instance).Dispose();

            // Only read from disk after Dispose releases the file handle, to avoid racing the
            // OS over file sharing; Dispose does not change what was already flushed above.
            string[] files = System.IO.Directory.GetFiles(logDirectory, "*.log");
            string contents = System.IO.File.ReadAllText(files.Single());
            if (!contents.Contains("immediate-error-probe"))
                throw new Exception("An Error line must be on disk immediately after Log() returns, with no wait for the background writer.");
        }
        finally
        {
            System.IO.Directory.Delete(logDirectory, true);
        }
        Console.WriteLine("PASS: RollingFileLog writes and flushes Error immediately, without waiting for the background writer.");
    }

    // Overflowing the bounded queue must never block the caller, must count the drops, and must
    // report the count exactly once, as soon as space frees up.
    private static void RunRollingFileLogOverflowCheck()
    {
        Type rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(logDirectory);
        try
        {
            const int capacity = 3;
            const int extra = 5;
            object instance = CreateRollingFileLog(rollingLogType, logDirectory, long.MaxValue, capacity);
            var log = (ILog)instance;
            FieldInfo writerGateField = rollingLogType.GetField("_writerGate", NonPublicInstance);
            FieldInfo droppedField = rollingLogType.GetField("_droppedQueuedLines", NonPublicInstance);
            var writerGate = (ManualResetEventSlim)writerGateField.GetValue(instance);

            // Pause the writer thread so the queue actually fills up, deterministically, instead
            // of racing a live consumer.
            writerGate.Reset();
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < capacity + extra; i++)
                    log.Log(null, new LogEventArgs(LogSeverity.Info, $"overflow-{i}", null, DateTime.Now));
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > 500)
                    throw new Exception("Queue overflow must never block the caller.");

                long dropped = (long)droppedField.GetValue(instance);
                if (dropped != extra)
                    throw new Exception($"Expected {extra} dropped lines, got {dropped}.");
            }
            finally
            {
                writerGate.Set();
            }

            if (!WaitForIdle(rollingLogType, instance, 2000))
                throw new Exception("Writer thread did not drain within the bound after resuming.");

            // The overflow report fires on the next successful enqueue once space has freed up.
            log.Log(null, new LogEventArgs(LogSeverity.Info, "after-overflow", null, DateTime.Now));
            long droppedAfterReport = (long)droppedField.GetValue(instance);
            if (droppedAfterReport != 0)
                throw new Exception("The dropped-line counter must reset once the overflow is reported.");

            ((IDisposable)instance).Dispose();

            string[] files = System.IO.Directory.GetFiles(logDirectory, "*.log");
            string contents = string.Join("\n", files.Select(System.IO.File.ReadAllText));
            if (!contents.Contains($"dropped {extra} line(s)"))
                throw new Exception("Expected a one-time report of the dropped line count once space freed up.");
        }
        finally
        {
            System.IO.Directory.Delete(logDirectory, true);
        }
        Console.WriteLine("PASS: queue overflow never blocks the caller, counts drops, and reports them once when space frees up.");
    }

    // Dispose must drain the writer thread within a bounded wait, not block indefinitely, and
    // nothing queued before Dispose is lost.
    private static void RunRollingFileLogDisposeDrainCheck()
    {
        Type rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-logtest-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(logDirectory);
        try
        {
            const int count = 200;
            object instance = CreateRollingFileLog(rollingLogType, logDirectory, long.MaxValue, count + 10);
            var log = (ILog)instance;

            for (int i = 0; i < count; i++)
                log.Log(null, new LogEventArgs(LogSeverity.Info, $"dispose-drain-{i}", null, DateTime.Now));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ((IDisposable)instance).Dispose();
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 2500)
                throw new Exception($"Dispose must drain within its bound; took {stopwatch.ElapsedMilliseconds}ms.");

            string[] files = System.IO.Directory.GetFiles(logDirectory, "*.log");
            string contents = string.Join("\n", files.Select(System.IO.File.ReadAllText));
            for (int i = 0; i < count; i++)
                if (!contents.Contains($"dispose-drain-{i}"))
                    throw new Exception($"Dispose must drain everything queued before it ran; missing line {i}.");
        }
        finally
        {
            System.IO.Directory.Delete(logDirectory, true);
        }
        Console.WriteLine("PASS: Dispose drains the queued backlog within its bound.");
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

    // Debug's [InterpolatedStringHandler] overload must skip the interpolation holes entirely
    // when Debug is disabled - not just skip logging the already-formatted string.
    private static void RunShellLoggerLazyDebugFormattingCheck()
    {
        LogSeverity original = ShellLogger.Severity;
        try
        {
            var probe = new ToStringCountingProbe();

            ShellLogger.Severity = LogSeverity.Info;
            ShellLogger.Debug($"lazy debug probe: {probe}");
            if (probe.ToStringCallCount != 0)
                throw new Exception("Debug's interpolation holes must not be evaluated while Debug is disabled.");

            ShellLogger.Severity = LogSeverity.Debug;
            ShellLogger.Debug($"lazy debug probe: {probe}");
            if (probe.ToStringCallCount == 0)
                throw new Exception("Debug's interpolation holes must be evaluated while Debug is enabled.");
        }
        finally
        {
            ShellLogger.Severity = original;
        }
        Console.WriteLine("PASS: ShellLogger.Debug's interpolated string handler skips formatting entirely while Debug is disabled.");
    }

    private sealed class ToStringCountingProbe
    {
        public int ToStringCallCount;
        public override string ToString()
        {
            ToStringCallCount++;
            return "probe";
        }
    }
}
