using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UltraWinBar.Utilities
{
    public class SettingsManager<T> : INotifyPropertyChanged where T : IMigratableSettings
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private static JsonSerializerOptions options = new()
        {
            IgnoreReadOnlyProperties = true,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private string _fileName;
        private readonly T _defaultSettings;
        private bool _preserveUnreadableFile;
        private bool _unreadableFileBackupCreated;
        private readonly object _saveLock = new object();
        private Timer _saveTimer;
        private bool _dirty;
        private string _pendingSave;
        private Task _writeTask = Task.CompletedTask;
        // Thread that mutates Settings; serialization is posted there so it never races a mutation.
        private SynchronizationContext _ownerContext;
        internal long SerializeCount;

        private T _settings;
        public T Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
                SaveToFile();
            }
        }

        public SettingsManager(string fileName, T defaultSettings)
        {
            _fileName = fileName;
            _defaultSettings = defaultSettings;
            _settings = defaultSettings;

            if (!LoadFromFile())
            {
                ShellLogger.Info("SettingsManager: Using default settings");
            }
        }

        private bool LoadFromFile()
        {
            try
            {
                if (!ShellHelper.Exists(_fileName))
                {
                    return false;
                }

                string jsonString = File.ReadAllText(_fileName);
                T loadedSettings = JsonSerializer.Deserialize<T>(jsonString);
                if (loadedSettings is null)
                {
                    _settings = _defaultSettings;
                    _preserveUnreadableFile = true;
                    ShellLogger.Warning("SettingsManager: Settings file contained null; defaults are active.");
                    return false;
                }

                _settings = loadedSettings;

                if (_settings.MigrationPerformed)
                {
                    // Save post-migration state so that we don't need to migrate every startup
                    SaveToFile();
                }

                return true;
            }
            catch (Exception ex)
            {
                _settings = _defaultSettings;
                _preserveUnreadableFile = true;
                ShellLogger.Error($"SettingsManager: Error loading settings file: {ex.Message}");
                return false;
            }
        }

        private void SaveToFile()
        {
            lock (_saveLock)
            {
                // Settings is created before the UI dispatcher runs, so the owner context is taken from the first change made under one.
                _ownerContext ??= SynchronizationContext.Current;
                _dirty = true;
                _saveTimer ??= new Timer(SaveTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(150, Timeout.Infinite);
            }
        }

        private void SaveTimerElapsed(object state)
        {
            if (_ownerContext != null)
            {
                _ownerContext.Post(_ => SerializeAndQueue(), null);
            }
            else
            {
                SerializeAndQueue();
            }
        }

        private void SerializeAndQueue()
        {
            lock (_saveLock)
            {
                if (!_dirty)
                {
                    return;
                }
                _dirty = false;
            }

            string jsonString = SerializeNow();
            if (jsonString == null)
            {
                return;
            }

            lock (_saveLock)
            {
                _pendingSave = jsonString;
                QueuePendingSave();
            }
        }

        private string SerializeNow()
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(Settings, options);
                Interlocked.Increment(ref SerializeCount);
                return jsonString;
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"SettingsManager: Error serializing settings: {ex.Message}");
                return null;
            }
        }

        private void QueuePendingSave()
        {
            if (_pendingSave == null)
            {
                return;
            }

            string jsonString = _pendingSave;
            _pendingSave = null;
            _writeTask = _writeTask.ContinueWith(_ => WriteToFile(jsonString), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
        }

        public void Flush()
        {
            Task writeTask;
            lock (_saveLock)
            {
                _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _dirty = false;
            }

            // Serialize inline on the calling (UI) thread: Flush runs during shutdown, whose
            // thread is not pumping its message loop, so posting to _ownerContext and waiting
            // on it here would deadlock.
            string jsonString = SerializeNow();

            lock (_saveLock)
            {
                if (jsonString != null)
                {
                    _pendingSave = jsonString;
                }
                QueuePendingSave();
                writeTask = _writeTask;
            }

            writeTask.GetAwaiter().GetResult();
        }

        private void WriteToFile(string jsonString)
        {
            string temporaryFileName = null;
            try
            {
                string directoryName = Path.GetDirectoryName(Path.GetFullPath(_fileName));
                Directory.CreateDirectory(directoryName);

                if (_preserveUnreadableFile && !_unreadableFileBackupCreated && File.Exists(_fileName))
                {
                    string backupFileName = Path.Combine(directoryName,
                        $"{Path.GetFileName(_fileName)}.corrupt-{Guid.NewGuid():N}");
                    File.Copy(_fileName, backupFileName);
                    _unreadableFileBackupCreated = true;
                    ShellLogger.Warning("SettingsManager: Preserved unreadable settings file for recovery.");
                }

                temporaryFileName = Path.Combine(directoryName, Path.GetRandomFileName());
                File.WriteAllText(temporaryFileName, jsonString);

                if (File.Exists(_fileName))
                {
                    File.Replace(temporaryFileName, _fileName, null);
                }
                else
                {
                    File.Move(temporaryFileName, _fileName);
                }

                temporaryFileName = null;
                _preserveUnreadableFile = false;
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"SettingsManager: Error saving settings file: {ex.Message}");
            }
            finally
            {
                if (temporaryFileName != null && File.Exists(temporaryFileName))
                {
                    try
                    {
                        File.Delete(temporaryFileName);
                    }
                    catch (Exception ex)
                    {
                        ShellLogger.Error($"SettingsManager: Error cleaning up temporary settings file: {ex.Message}");
                    }
                }
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public interface IMigratableSettings
    {
        public bool MigrationPerformed { get; }
    }
}
