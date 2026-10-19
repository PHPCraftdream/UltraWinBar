using System;
using System.Collections.Generic;
using System.Diagnostics;
using ManagedShell.Common.Logging;

namespace UltraWinBar.Utilities
{
    // Startup stages with the time since the process was created, so a slow logon start can be pinned on a stage.
    // Marks made before the logger exists are held and written when it opens.
    internal sealed class StartupTimeline
    {
        private static readonly StartupTimeline Current = Create();

        private readonly Func<long> _elapsedMs;
        private readonly Action<string> _write;
        private readonly object _gate = new object();
        private List<string> _held = new List<string>();
        private long _last;

        internal StartupTimeline(Func<long> elapsedMs, Action<string> write)
        {
            _elapsedMs = elapsedMs;
            _write = write;
        }

        internal static void Mark(string stage) => Current.Record(stage);

        internal static void OpenLog() => Current.Open();

        internal void Record(string stage)
        {
            lock (_gate)
            {
                long now = _elapsedMs();
                string line = Format(stage, now, now - _last);
                _last = now;
                if (_held != null) _held.Add(line);
                else _write(line);
            }
        }

        internal void Open()
        {
            lock (_gate)
            {
                List<string> held = _held;
                _held = null;
                if (held == null) return;
                foreach (string line in held) _write(line);
            }
        }

        internal static string Format(string stage, long ms, long sinceLast) => $"Startup: {stage} at {ms} ms (+{sinceLast} ms)";

        private static StartupTimeline Create()
        {
            long age = ProcessAgeMs();
            Stopwatch clock = Stopwatch.StartNew();
            return new StartupTimeline(() => age + clock.ElapsedMilliseconds, line => ShellLogger.Info(line));
        }

        private static long ProcessAgeMs()
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                return Math.Clamp((long)(DateTime.Now - process.StartTime).TotalMilliseconds, 0, 600_000);
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
