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
        private static readonly TimeSpan WorkAreaRecoveryDelay = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan WorkAreaBroadcastTimeout = TimeSpan.FromSeconds(3);

        private bool _isSettingDisplays;
        private bool _isOpeningTaskbars;
        private bool _reopenTaskbarsPending;
        private bool _manualWorkArea;
        private bool _workAreaWatchdogStarted;
        private int _pendingDisplayEvents;
        private List<AppBarScreen> _screenState = new List<AppBarScreen>();
        private List<Taskbar> _taskbars = new List<Taskbar>();
        private NativeMethods.Rect _originalWorkArea;
        private NativeMethods.Rect _originalMonitorBounds;
        private Process _workAreaWatchdogProcess;
        private NativeMethods.Rect? _expectedWorkArea;
        private WindowPlacementGuard _placementGuard;
        private readonly WorkAreaRecovery _workAreaRecovery = new WorkAreaRecovery();
        private readonly DispatcherTimer _workAreaRecoveryTimer;
        private bool _disposed;

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
            _workAreaRecoveryTimer = new DispatcherTimer(DispatcherPriority.Background);
            _workAreaRecoveryTimer.Tick += RecoverWorkArea;

            NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.GETWORKAREA, 0, ref _originalWorkArea, 0);
            _originalMonitorBounds = ToRect(AppBarScreen.FromPrimaryScreen().Bounds);

            _shellManager.ExplorerHelper.HideExplorerTaskbar = true;

            openTaskbars();

            _placementGuard = new WindowPlacementGuard(QueueManualWorkAreaRecovery);

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
                RequestReopenTaskbars();
            }
            else if (e.PropertyName == nameof(Settings.ShowMultiMon))
            {
                // Update screen state in case it has changed since last checked
                _screenState = AppBarScreen.FromAllScreens();

                if (_screenState.Count < 2)
                {
                    return;
                }

                RequestReopenTaskbars();
            }
            else if (e.PropertyName == nameof(Settings.AutoHide))
            {
                RequestReopenTaskbars();
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

        // One reopen per burst: every panel reacts to the same settings change.
        public void RequestReopenTaskbars()
        {
            if (_reopenTaskbarsPending)
            {
                return;
            }

            _reopenTaskbarsPending = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                _reopenTaskbarsPending = false;
                ReopenTaskbars();
            });
        }

        public void NotifyWorkAreaChange()
        {
            if (_isOpeningTaskbars)
            {
                return;
            }

            if (_manualWorkArea)
            {
                QueueManualWorkAreaRecovery();
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
                // K19: remember it weakly so a missed unsubscribe anywhere shows up in the health log.
                PanelLeakTracker.TrackClosed(taskbar);
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
                _expectedWorkArea = null;
            }
            _manualWorkArea = useManualWorkArea;

            if (_manualWorkArea && !_workAreaWatchdogStarted)
            {
                _workAreaWatchdogProcess = Program.StartWorkAreaWatchdog(_originalWorkArea);
                _workAreaWatchdogStarted = true;
            }

            ShellLogger.Debug($"WindowManager: Opening taskbars");
            ResetWorkAreaRecovery();
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
                QueueManualWorkAreaRecovery();
            }
        }

        private void ApplyManualLayout(List<Taskbar> taskbars, bool completeDeferredLayout)
        {
            if (!_manualWorkArea || _screenState.Count != 1)
            {
                return;
            }

            AppBarScreen screen = _screenState[0];
            NativeMethods.Rect screenRect = ToRect(screen.Bounds);

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

            if (!_expectedWorkArea.HasValue || !_expectedWorkArea.Value.Equals(layout.WorkArea))
            {
                ResetWorkAreaRecovery();
                _expectedWorkArea = layout.WorkArea;
                WorkAreaManager.Apply(layout.WorkArea, (uint)Process.GetCurrentProcess().Id);
            }
            else QueueManualWorkAreaRecovery();
            ShellLogger.Debug($"WindowManager: Applied manual work area {FormatRect(layout.WorkArea)}");
        }

        private void ResetWorkAreaRecovery()
        {
            _workAreaRecoveryTimer.Stop();
            _workAreaRecovery.Reset();
        }

        private void QueueManualWorkAreaRecovery()
        {
            if (_disposed || !_manualWorkArea || _isOpeningTaskbars || !_expectedWorkArea.HasValue ||
                _workAreaRecoveryTimer.IsEnabled) return;
            _workAreaRecoveryTimer.Interval = WorkAreaRecoveryDelay;
            _workAreaRecoveryTimer.Start();
        }

        private void RecoverWorkArea(object sender, EventArgs e)
        {
            _workAreaRecoveryTimer.Stop();
            if (_disposed || !_manualWorkArea || _isOpeningTaskbars || !_expectedWorkArea.HasValue ||
                !WorkAreaManager.TryGetCurrent(out var actual)) return;
            var expected = _expectedWorkArea.Value;
            long now = Environment.TickCount64;
            var result = _workAreaRecovery.Recover(now,
                () => actual.Equals(expected),
                () => WorkAreaManager.Apply(expected, (uint)Process.GetCurrentProcess().Id));
            if (result == WorkAreaRecoveryResult.Applied)
                ShellLogger.Warning($"WindowManager: Restored work area from {FormatRect(actual)} to {FormatRect(expected)}");
            else if (result == WorkAreaRecoveryResult.Suspended)
                ShellLogger.Warning($"WindowManager: Repeated work-area conflict; recovery paused for {_workAreaRecovery.SuspendedFor / 1000}s.");
            if (result == WorkAreaRecoveryResult.Deferred || result == WorkAreaRecoveryResult.Suspended)
            {
                _workAreaRecoveryTimer.Interval = TimeSpan.FromMilliseconds(
                    Math.Max(_workAreaRecovery.RetryAt - now, WorkAreaRecoveryDelay.TotalMilliseconds));
                _workAreaRecoveryTimer.Start();
            }
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

            UpdateOriginalWorkArea(newScreens);
            return true;
        }

        // The primary monitor's bounds may have moved/resized (resolution, monitor swap, DPI).
        // _originalWorkArea was captured against the old bounds, so it no longer describes
        // "the work area other appbars/Explorer's taskbar would leave us on the current monitor".
        // Re-derive it by keeping the insets (what others reserved) fixed and re-applying them
        // to the new monitor bounds, rather than trusting the live SPI value, which in manual
        // mode reflects our own last-applied layout rather than the true original.
        private void UpdateOriginalWorkArea(List<AppBarScreen> newScreens)
        {
            AppBarScreen primary = newScreens.Find(s => s.Primary) ?? (newScreens.Count > 0 ? newScreens[0] : null);
            if (primary == null)
            {
                return;
            }

            NativeMethods.Rect newBounds = ToRect(primary.Bounds);
            if (newBounds.Equals(_originalMonitorBounds))
            {
                return;
            }

            NativeMethods.Rect recomputed = TranslateWorkArea(_originalWorkArea, _originalMonitorBounds, newBounds);

            ShellLogger.Debug($"WindowManager: Original work area recomputed from {FormatRect(_originalWorkArea)} to {FormatRect(recomputed)} (monitor {FormatRect(_originalMonitorBounds)} -> {FormatRect(newBounds)})");

            _originalWorkArea = recomputed;
            _originalMonitorBounds = newBounds;

            RestartWorkAreaWatchdogIfNeeded();
        }

        internal static NativeMethods.Rect TranslateWorkArea(NativeMethods.Rect workArea, NativeMethods.Rect oldBounds, NativeMethods.Rect newBounds)
        {
            NativeMethods.Rect recomputed = new NativeMethods.Rect
            {
                Left = newBounds.Left + (workArea.Left - oldBounds.Left),
                Top = newBounds.Top + (workArea.Top - oldBounds.Top),
                Right = newBounds.Right - (oldBounds.Right - workArea.Right),
                Bottom = newBounds.Bottom - (oldBounds.Bottom - workArea.Bottom)
            };

            // Insets no longer fit the new bounds (e.g. DPI change skewed them) - fall back to
            // the full monitor rather than ship a degenerate/inverted rect.
            return recomputed.Right <= recomputed.Left || recomputed.Bottom <= recomputed.Top ? newBounds : recomputed;
        }

        private void RestartWorkAreaWatchdogIfNeeded()
        {
            // Not started yet: the next transition into manual mode will start it with the
            // now-current _originalWorkArea, nothing to do.
            if (!_workAreaWatchdogStarted)
            {
                return;
            }

            _workAreaWatchdogProcess = Program.RestartWorkAreaWatchdog(_workAreaWatchdogProcess, _originalWorkArea);
        }

        private static NativeMethods.Rect ToRect(System.Drawing.Rectangle bounds)
        {
            return new NativeMethods.Rect
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Right = bounds.Right,
                Bottom = bounds.Bottom
            };
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
            _disposed = true;
            _workAreaRecoveryTimer.Stop();
            _workAreaRecoveryTimer.Tick -= RecoverWorkArea;
            _placementGuard?.Dispose();
            if (_manualWorkArea)
            {
                WorkAreaManager.Apply(_originalWorkArea, (uint)Process.GetCurrentProcess().Id);
                // Shutting down: wait for the broadcast so other apps see the restored work area before we exit.
                WorkAreaManager.WaitForPendingNotifications(WorkAreaBroadcastTimeout);
            }
            _shellManager.ExplorerHelper.HideExplorerTaskbar = false;
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }
    }
}
