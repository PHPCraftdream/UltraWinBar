using ManagedShell.Common.Logging;
using ManagedShell.Common.Logging.Observers;
using System;
using System.IO;

namespace UltraWinBar.Utilities
{
    class ManagedShellLogger : IDisposable
    {
        private string _logPath = "Logs".InLocalAppData();
        private string _logName = DateTime.Now.ToString("yyyy-MM-dd_HHmmssfff");
        private string _logExt = "log";
        private TimeSpan _logRetention = new TimeSpan(7, 0, 0);
        private FileLog _fileLog;
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

            _filteredConsoleLog = new FilteredLog(new ConsoleLog(), Settings.Instance.DebugLogMasks);
            ShellLogger.Attach(_filteredConsoleLog, true);
        }

        private void SetupFileLog()
        {
            DeleteOldLogFiles();

            try
            {
                _fileLog = new FileLog(Path.Combine(_logPath, $"{_logName}.{_logExt}"));
                _fileLog?.Open();

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

        private void DeleteOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(_logPath))
                {
                    // Nothing to delete
                    return;
                }

                // look for all of the log files
                DirectoryInfo info = new DirectoryInfo(_logPath);
                FileInfo[] files = info.GetFiles($"*.{_logExt}", SearchOption.TopDirectoryOnly);

                // delete any files that are older than the retention period
                DateTime now = DateTime.Now;
                foreach (FileInfo file in files)
                {
                    if (now.Subtract(file.LastWriteTime) > _logRetention)
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
}
