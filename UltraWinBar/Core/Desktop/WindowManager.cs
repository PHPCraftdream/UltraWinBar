using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    public class WindowManager : IDisposable
    {
        private static object reopenLock = new object();

        private bool _isSettingDisplays;
        private bool _isOpeningTaskbars;
        private bool _manualWorkArea;
        private bool _workAreaWatchdogStarted;
        private int _pendingDisplayEvents;
        private List<AppBarScreen> _screenState = new List<AppBarScreen>();
        private List<Taskbar> _taskbars = new List<Taskbar>();
        private NativeMethods.Rect _originalWorkArea;
        private WindowPlacementGuard _placementGuard;

        private readonly DictionaryManager _dictionaryManager;
        private readonly ExplorerMonitor _explorerMonitor;
        private readonly StartMenuMonitor _startMenuMonitor;
        private readonly ShellManager _shellManager;
        private readonly Updater _updater;
        private HotkeyManager _hotkeyManager;

        internal bool UsesManualWorkArea => _manualWorkArea;

        public WindowManager(DictionaryManager dictionaryManager, ExplorerMonitor explorerMonitor, ShellManager shellManager, StartMenuMonitor startMenuMonitor, Updater updater, HotkeyManager hotkeyManager)
        {
            _dictionaryManager = dictionaryManager;
            _explorerMonitor = explorerMonitor;
            _shellManager = shellManager;
            _startMenuMonitor = startMenuMonitor;
            _updater = updater;
            _hotkeyManager = hotkeyManager;

            NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.GETWORKAREA, 0, ref _originalWorkArea, 0);

            _shellManager.ExplorerHelper.HideExplorerTaskbar = true;

            openTaskbars();

            _placementGuard = new WindowPlacementGuard();

            _explorerMonitor.ExplorerMonitorStart(this, _shellManager);

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.AdditionalEdges) ||
                e.PropertyName == nameof(Settings.EdgePriority) ||
                e.PropertyName == nameof(Settings.Edge))
            {
                // Adding/removing a taskbar, or restretching one, requires re-registering
                // all of them so the OS AppBar API recomputes who shrinks for whom.
                ReopenTaskbars();
            }
            else if (e.PropertyName == nameof(Settings.ShowMultiMon))
            {
                // Update screen state in case it has changed since last checked
                _screenState = AppBarScreen.FromAllScreens();

                if (_screenState.Count < 2)
                {
                    return;
                }

                ReopenTaskbars();
            }
            else if (e.PropertyName == nameof(Settings.AutoHide))
            {
                ReopenTaskbars();
            }
            else if (_manualWorkArea &&
                (e.PropertyName == nameof(Settings.Theme) ||
                 e.PropertyName == nameof(Settings.TaskbarScale) ||
                 e.PropertyName == nameof(Settings.LockTaskbar) ||
                 e.PropertyName == nameof(Settings.RowCount) ||
                 e.PropertyName == nameof(Settings.TaskbarWidth) ||
                 e.PropertyName == nameof(Settings.EdgeSizes)))
            {
                ApplyManualLayout(_taskbars, false);
            }
        }

        public void ReopenTaskbars()
        {
            lock (reopenLock)
            {
                closeTaskbars();
                openTaskbars();
            }
        }

        public void NotifyWorkAreaChange()
        {
            if (_isOpeningTaskbars || _manualWorkArea)
            {
                return;
            }

            ShellLogger.Debug($"WindowManager: Work area change notification received");
            handleDisplayChange();
        }

        internal void RefreshManualLayout()
        {
            if (_manualWorkArea && !_isOpeningTaskbars)
                ApplyManualLayout(_taskbars, false);
        }

        public void NotifyDisplayChange(ScreenSetupReason reason)
        {
            ShellLogger.Debug($"WindowManager: Display change notification received ({reason})");
            handleDisplayChange();
        }

        private void handleDisplayChange()
        {
            _pendingDisplayEvents++;

            if (_isSettingDisplays)
            {
                return;
            }

            _isSettingDisplays = true;
            try
            {
                while (_pendingDisplayEvents > 0)
                {
                    // Skip re-opening taskbars if the screens haven't changed
                    if (!haveDisplaysChanged())
                    {
                        _pendingDisplayEvents--;
                        continue;
                    }

                    ReopenTaskbars();

                    _pendingDisplayEvents--;
                }

                ShellLogger.Debug($"WindowManager: Finished processing display events");
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"WindowManager: Error processing display events: {ex}");
            }
            finally
            {
                _isSettingDisplays = false;
            }
        }

        public bool IsValidHMonitor(IntPtr hMonitor)
        {
            foreach(var screen in _screenState)
            {
                if (screen.HMonitor == hMonitor)
                {
                    return true;
                }
            }

            return false;
        }

        private void closeTaskbars()
        {
            ShellLogger.Debug($"WindowManager: Closing all taskbars");

            foreach (var taskbar in _taskbars)
            {
                taskbar.AllowClose = true;
                taskbar.Close();
            }

            _taskbars.Clear();
        }

        private void openTaskbars()
        {
            _screenState = AppBarScreen.FromAllScreens();
            bool useManualWorkArea = _screenState.Count == 1 && !Settings.Instance.AutoHide;
            if (_manualWorkArea && !useManualWorkArea)
            {
                WorkAreaManager.Apply(_originalWorkArea, (uint)Process.GetCurrentProcess().Id);
            }
            _manualWorkArea = useManualWorkArea;

            if (_manualWorkArea && !_workAreaWatchdogStarted)
            {
                Program.StartWorkAreaWatchdog(_originalWorkArea);
                _workAreaWatchdogStarted = true;
            }

            ShellLogger.Debug($"WindowManager: Opening taskbars");
            _isOpeningTaskbars = true;

            try
            {
                List<Taskbar> pendingTaskbars = new List<Taskbar>();

                if (Settings.Instance.ShowMultiMon)
                {
                    foreach (var screen in _screenState)
                    {
                        createTaskbarsOnScreen(screen, pendingTaskbars);
                    }
                }
                else
                {
                    createTaskbarsOnScreen(AppBarScreen.FromPrimaryScreen(), pendingTaskbars);
                }

                foreach (Taskbar taskbar in pendingTaskbars)
                {
                    taskbar.Opacity = 0;
                    taskbar.ShowActivated = false;
                    taskbar.Show();
                    _taskbars.Add(taskbar);
                }

                if (_manualWorkArea)
                {
                    ApplyManualLayout(pendingTaskbars, true);
                }
                else
                {
                    ShellLogger.Debug($"WindowManager: Registering {pendingTaskbars.Count} preloaded taskbars");
                    Stopwatch registrationTimer = Stopwatch.StartNew();
                    LogSeverity previousSeverity = ShellLogger.Severity;
                    List<string> registrationTimings = new List<string>();
                    try
                    {
                        if (previousSeverity == LogSeverity.Debug)
                        {
                            ShellLogger.Severity = LogSeverity.Info;
                        }

                        using (Dispatcher.CurrentDispatcher.DisableProcessing())
                        {
                            foreach (Taskbar taskbar in pendingTaskbars)
                            {
                                taskbar.CompleteDeferredRegistration();
                                registrationTimings.Add($"{taskbar.AppBarEdge}={registrationTimer.ElapsedMilliseconds}ms");
                            }

                            foreach (Taskbar taskbar in pendingTaskbars)
                            {
                                taskbar.UpdatePosition();
                            }
                        }
                    }
                    finally
                    {
                        ShellLogger.Severity = previousSeverity;
                    }
                    ShellLogger.Debug($"WindowManager: Registration batch completed in {registrationTimer.ElapsedMilliseconds}ms ({string.Join(", ", registrationTimings)})");
                }

                foreach (Taskbar taskbar in pendingTaskbars)
                {
                    taskbar.Opacity = 1;
                }
            }
            finally
            {
                _isOpeningTaskbars = false;
            }
        }

        private void ApplyManualLayout(List<Taskbar> taskbars, bool completeDeferredLayout)
        {
            if (!_manualWorkArea || _screenState.Count != 1)
            {
                return;
            }

            AppBarScreen screen = _screenState[0];
            NativeMethods.Rect screenRect = new NativeMethods.Rect
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Right = screen.Bounds.Right,
                Bottom = screen.Bounds.Bottom
            };

            var layout = PanelLayout.Calculate(screenRect,
                Settings.Instance.ResolvedEdgeOrder.FindAll(edge => taskbars.Exists(candidate => candidate.AppBarEdge == edge)),
                edge => taskbars.Find(candidate => candidate.AppBarEdge == edge).DesiredThicknessPixels);

            foreach (var (edge, taskbarRect) in layout.Panels)
            {
                Taskbar taskbar = taskbars.Find(candidate => candidate.AppBarEdge == edge);
                if (completeDeferredLayout)
                {
                    taskbar.CompleteDeferredStandaloneLayout(taskbarRect);
                }
                else
                {
                    taskbar.SetStandaloneLayout(taskbarRect);
                }
            }

            WorkAreaManager.Apply(layout.WorkArea, (uint)Process.GetCurrentProcess().Id);
            ShellLogger.Debug($"WindowManager: Applied manual work area {FormatRect(layout.WorkArea)}");
        }

        private static string FormatRect(NativeMethods.Rect rect)
        {
            return $"({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})";
        }

        private void createTaskbarsOnScreen(AppBarScreen screen, List<Taskbar> taskbars)
        {
            AppBarEdge primaryEdge = Settings.Instance.Edge;

            // Registration order matters: the OS AppBar API shrinks each new bar to avoid
            // ones already registered, so ResolvedEdgeOrder (not just EnabledEdges) decides
            // who keeps full length and who yields.
            foreach (AppBarEdge edge in Settings.Instance.ResolvedEdgeOrder)
            {
                createTaskbar(screen, edge, edge == primaryEdge, taskbars);
            }
        }

        private void createTaskbar(AppBarScreen screen, AppBarEdge edge, bool isPrimaryEdge, List<Taskbar> taskbars)
        {
            ShellLogger.Debug($"WindowManager: Preloading taskbar on screen {screen.DeviceName} at edge {edge}");
            Taskbar taskbar = new Taskbar(this, _dictionaryManager, _shellManager, _startMenuMonitor, _updater, _hotkeyManager,
                screen, edge, Settings.Instance.AutoHide ? AppBarMode.AutoHide : AppBarMode.Normal, isPrimaryEdge, true);
            taskbars.Add(taskbar);
        }

        private bool haveDisplaysChanged()
        {
            resetScreenCache();

            var newScreens = AppBarScreen.FromAllScreens();

            if (_screenState.Count == newScreens.Count)
            {
                bool same = true;
                for (int i = 0; i < newScreens.Count; i++)
                {
                    AppBarScreen current = newScreens[i];
                    if (!(_screenState[i].Bounds == current.Bounds && _screenState[i].DeviceName == current.DeviceName && _screenState[i].Primary == current.Primary && _screenState[i].HMonitor == current.HMonitor))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    ShellLogger.Debug("WindowManager: No display changes");
                    return false;
                }
            }

            return true;
        }

        private void resetScreenCache()
        {
            // use reflection to empty screens cache
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var fi = typeof(Screen).GetField("screens", flags) ?? typeof(Screen).GetField("s_screens", flags)
                ?? throw new Exception("Can't find & reset screens cache inside winforms");
            fi.SetValue(null, null);
        }

        public void Dispose()
        {
            _placementGuard?.Dispose();
            if (_manualWorkArea)
            {
                WorkAreaManager.Apply(_originalWorkArea, (uint)Process.GetCurrentProcess().Id);
            }
            _shellManager.ExplorerHelper.HideExplorerTaskbar = false;
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }
    }
}
