using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace ManagedShell.Common.Logging
{
    public static class ShellLogger
    {
        // UltraWinBar: interpolated string handlers so `Debug($"...")` / `Info($"...")` don't
        // build the string (or evaluate the holes) when that severity is disabled. Overload
        // resolution prefers a handler-typed parameter over `string` for an interpolated string
        // literal argument, so existing call sites pick these up without any source change.
        [InterpolatedStringHandler]
        public struct DebugInterpolatedStringHandler
        {
            private StringBuilder _builder;

            public DebugInterpolatedStringHandler(int literalLength, int formattedCount, out bool shouldAppend)
            {
                shouldAppend = _isDebug;
                _builder = shouldAppend ? new StringBuilder(literalLength + formattedCount * 11) : null;
            }

            public void AppendLiteral(string value) => _builder.Append(value);
            public void AppendFormatted<T>(T value) => _builder.Append(value);
            public void AppendFormatted<T>(T value, string format) where T : IFormattable => _builder.Append(value?.ToString(format, null));

            internal string GetFormattedTextOrDefault() => _builder?.ToString() ?? string.Empty;
        }

        [InterpolatedStringHandler]
        public struct InfoInterpolatedStringHandler
        {
            private StringBuilder _builder;

            public InfoInterpolatedStringHandler(int literalLength, int formattedCount, out bool shouldAppend)
            {
                shouldAppend = _isInfo;
                _builder = shouldAppend ? new StringBuilder(literalLength + formattedCount * 11) : null;
            }

            public void AppendLiteral(string value) => _builder.Append(value);
            public void AppendFormatted<T>(T value) => _builder.Append(value);
            public void AppendFormatted<T>(T value, string format) where T : IFormattable => _builder.Append(value?.ToString(format, null));

            internal string GetFormattedTextOrDefault() => _builder?.ToString() ?? string.Empty;
        }

        #region Delegates

        /// <summary>
        /// Delegate event handler that hooks up requests.
        /// </summary>
        /// <param name="sender">Sender of the event.</param>
        /// <param name="e">Event arguments.</param>
        /// <remarks>
        /// GoF Design Pattern: Observer, Singleton.
        /// The Observer Design Pattern allows Observer classes to attach itself to 
        /// this Logger class and be notified if certain events occur. 
        /// 
        /// The Singleton Design Pattern allows the application to have just one
        /// place that is aware of the application-wide LogSeverity setting.
        /// </remarks>
        public delegate void LogEventHandler(object sender, LogEventArgs e);

        #endregion

        // These Booleans are used strictly to improve performance.
        private static bool _isDebug;
        private static bool _isError;
        private static bool _isFatal;
        private static bool _isInfo;
        private static bool _isWarning;
        private static LogSeverity _severity;

        // UltraWinBar: bounded ring buffer for events raised before any observer attaches: keeps
        // the newest MaxOrphanedEvents and counts the rest as dropped, instead of growing without
        // limit for the whole uptime if no observer ever attaches. Guarded by _orphanLock so
        // concurrent Debug/Info/... calls from multiple threads never corrupt it.
        private const int MaxOrphanedEvents = 500;
        private static readonly object _orphanLock = new object();
        private static readonly Queue<LogEventArgs> _orphanedEvents = new Queue<LogEventArgs>();
        private static long _droppedOrphanedEvents;

        /// <summary>
        /// Private constructor. Initializes default severity to "Debug".
        /// </summary>
        static ShellLogger()
        {
            // Default severity is Debug level
            Severity = LogSeverity.Debug;
        }

        /// <summary>
        /// Gets and sets the severity level of logging activity.
        /// </summary>
        public static LogSeverity Severity
        {
            get { return _severity; }
            set
            {
                _severity = value;

                // Set Booleans to help improve performance
                var severity = (int)_severity;

                _isDebug = ((int)LogSeverity.Debug) >= severity;
                _isInfo = ((int)LogSeverity.Info) >= severity;
                _isWarning = ((int)LogSeverity.Warning) >= severity;
                _isError = ((int)LogSeverity.Error) >= severity;
                _isFatal = ((int)LogSeverity.Fatal) >= severity;
            }
        }

        /// <summary>
        /// The Log event.
        /// </summary>
        public static event LogEventHandler Log;

        /// <summary>
        /// Log a message when severity level is "Debug" or higher.
        /// </summary>
        /// <param name="message">Log message</param>
        public static void Debug(string message)
        {
            // if (_isDebug) // Removed due to the same condition exisiting in the DebugIf call
            DebugIf(true, message, null);
        }

        /// <summary>
        /// Log a message when severity level is "Debug" or higher AND condition is met.
        /// </summary>
        /// <param name="message">Log message</param>
        public static void DebugIf(bool condition, string message)
        {
            // if (_isDebug) // Removed due to the same condition exisiting in the DebugIf call
            DebugIf(condition, message, null);
        }

        /// <summary>
        /// Log a message when severity level is "Debug" or higher.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="exception">Inner exception.</param>
        public static void Debug(string message, Exception exception)
        {
            // if (_isDebug) // Removed due to the same condition exisiting in the DebugIf call
            DebugIf(true, message, exception);
        }

        /// <summary>
        /// Log a message when severity level is "Debug" or higher AND condition is met.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="exception">Inner exception.</param>
        public static void DebugIf(bool condition, string message, Exception exception)
        {
            if (_isDebug && condition)
                OnLog(new LogEventArgs(LogSeverity.Debug, message, exception, DateTime.Now));
        }

        /// <summary>
        /// Log a message when severity level is "Debug" or higher. The interpolated string is
        /// only formatted when Debug is enabled.
        /// </summary>
        public static void Debug(ref DebugInterpolatedStringHandler message)
        {
            if (_isDebug)
                OnLog(new LogEventArgs(LogSeverity.Debug, message.GetFormattedTextOrDefault(), null, DateTime.Now));
        }

        /// <summary>
        /// Log a message when severity level is "Info" or higher.
        /// </summary>
        /// <param name="message">Log message</param>
        public static void Info(string message)
        {
            if (_isInfo)
                Info(message, null);
        }

        /// <summary>
        /// Log a message when severity level is "Info" or higher. The interpolated string is
        /// only formatted when Info is enabled.
        /// </summary>
        public static void Info(ref InfoInterpolatedStringHandler message)
        {
            if (_isInfo)
                OnLog(new LogEventArgs(LogSeverity.Info, message.GetFormattedTextOrDefault(), null, DateTime.Now));
        }

        /// <summary>
        /// Log a message when severity level is "Info" or higher.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="exception">Inner exception.</param>
        public static void Info(string message, Exception exception)
        {
            if (_isInfo)
                OnLog(new LogEventArgs(LogSeverity.Info, message, exception, DateTime.Now));
        }

        /// <summary>
        /// Log a message when severity level is "Warning" or higher.
        /// </summary>
        /// <param name="message">Log message.</param>
        public static void Warning(string message)
        {
            if (_isWarning)
                Warning(message, null);
        }

        /// <summary>
        /// Log a message when severity level is "Warning" or higher.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="exception">Inner exception.</param>
        public static void Warning(string message, Exception exception)
        {
            if (_isWarning)
                OnLog(new LogEventArgs(LogSeverity.Warning, message, exception, DateTime.Now));
        }

        /// <summary>
        /// Log a message when severity level is "Error" or higher.
        /// </summary>
        /// <param name="message">Log message</param>
        public static void Error(string message)
        {
            if (_isError)
                Error(message, null);
        }

        /// <summary>
        /// Log a message when severity level is "Error" or higher.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="exception">Inner exception.</param>
        public static void Error(string message, Exception exception)
        {
            if (_isError)
                OnLog(new LogEventArgs(LogSeverity.Error, message, exception, DateTime.Now));
        }

        /// <summary>
        /// Log a message when severity level is "Fatal"
        /// </summary>
        /// <param name="message">Log message</param>
        public static void Fatal(string message)
        {
            if (_isFatal)
                Fatal(message, null);
        }

        /// <summary>
        /// Log a message when severity level is "Fatal"
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="exception">Inner exception.</param>
        public static void Fatal(string message, Exception exception)
        {
            if (_isFatal)
                OnLog(new LogEventArgs(LogSeverity.Fatal, message, exception, DateTime.Now));
        }

        /// <summary>
        /// Invoke the Log event.
        /// </summary>
        /// <param name="e">Log event parameters.</param>
        public static void OnLog(LogEventArgs e)
        {
            LogEventHandler handler = Log;
            if (handler != null)
            {
                InvokeObservers(handler, e);
            }
            else
            {
                lock (_orphanLock)
                {
                    if (_orphanedEvents.Count >= MaxOrphanedEvents)
                    {
                        _orphanedEvents.Dequeue();
                        _droppedOrphanedEvents++;
                    }
                    _orphanedEvents.Enqueue(e);
                }
            }
        }

        // UltraWinBar: each observer is invoked independently. One observer throwing must not
        // stop the rest from being notified, and must not escape ShellLogger into the caller
        // (which can be a native callback).
        private static void InvokeObservers(LogEventHandler handler, LogEventArgs e)
        {
            foreach (LogEventHandler observer in handler.GetInvocationList())
            {
                try
                {
                    observer(null, e);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Attach a listening observer logging device to logger.
        /// </summary>
        /// <param name="observer">Observer (listening device).</param>
        /// <param name="flushOrphanedEvents">If this is the first observer, whether any log events emitted prior should be re-emitted.</param>
        public static void Attach(ILog observer, bool flushOrphanedEvents = false)
        {
            Log += observer.Log;

            if (flushOrphanedEvents) FlushOrphanedLogEvents();
        }

        /// <summary>
        /// Detach a listening observer logging device from logger.
        /// </summary>
        /// <param name="observer">Observer (listening device).</param>
        public static void Detach(ILog observer)
        {
            Log -= observer.Log;
        }

        public static void Attach(ILog[] observers, bool flushOrphanedEvents = false)
        {
            foreach (var observer in observers)
                Attach(observer);

            if (flushOrphanedEvents) FlushOrphanedLogEvents();
        }

        public static void Detach(ILog[] observers)
        {
            foreach (var observer in observers)
                Detach(observer);
        }

        /// <summary>
        /// Called from the Attach method to re-emit log events that occurred prior to the first observer being attached.
        /// </summary>
        private static void FlushOrphanedLogEvents()
        {
            // UltraWinBar: snapshot-and-clear under the lock, then dispatch outside it, so an
            // observer logging from within its own Log() can't deadlock or re-enter mid-mutation.
            LogEventArgs[] snapshot;
            lock (_orphanLock)
            {
                if (_orphanedEvents.Count == 0)
                {
                    return;
                }

                snapshot = new LogEventArgs[_orphanedEvents.Count];
                _orphanedEvents.CopyTo(snapshot, 0);
                _orphanedEvents.Clear();
            }

            foreach (var log in snapshot)
            {
                OnLog(log);
            }
        }
    }
}
