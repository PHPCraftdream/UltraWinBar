using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Common.Native;
using ManagedShell.Common.SupportingClasses;
using ManagedShell.UWPInterop;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    public partial class StartMenuMonitor : IDisposable
    {
        // Explorer-hosted: recreated lazily with backoff and on every Explorer restart.
        private readonly ShellComProxy<LauncherVisibility> _launcherVisibility;
        private DispatcherTimer _poller;
        private Action _correctPlacement;
        private IntPtr _positionedMenu;
        private WinEventHook _menuEventHook;
        private bool _correctingPlacement;
        private bool _isVisible;
        private IntPtr _taskbarHwndActivated;
        // Panel whose Start button opened the menu; unlike _taskbarHwndActivated, kept until the menu closes.
        private IntPtr _placementTaskbar;
        private IntPtr _positionedTaskbar;
        // Open Shell's menu takes focus as a hidden placeholder and is shown ~50 ms later; until then it is opening, not closed.
        private bool _positionedMenuShown;
        private int _openingTicks;
        private const int MaxOpeningTicks = 20;
        private int _staleActivationTicks;
        private readonly StartMenuFade _fade = new StartMenuFade();
        private bool _disposed;
        private readonly StartMenuAvatarGuard _avatarGuard = new StartMenuAvatarGuard();

        // Fast (100ms) poll only while the menu is visible, being positioned, or a Start button
        // press is pending a response; otherwise a slow safety poll, since LauncherVisibilityChanged
        // (Win10+ events) drives the common open/close transitions instantly.
        private static readonly TimeSpan FastPollInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan SlowPollInterval = TimeSpan.FromMilliseconds(1500);

        // Fast path for the modern Start menu: EVENT_SYSTEM_FOREGROUND fires the instant it
        // becomes the foreground window, instead of waiting for the next 100ms poller tick
        // (during which the window is already visibly painted at the OS's default position).
        // Kept as a supplement, not a replacement — the poller still drives Open Shell/classic
        // detection and close detection, where a 100ms delay isn't visually noticeable.
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
        private static bool IsCloaked(IntPtr hwnd) =>
            DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
        private IDisposable _foregroundSubscription;

        public event EventHandler<StartMenuMonitorEventArgs> StartMenuVisibilityChanged;

        // Owns the launcher visibility proxy so it can be recreated after explorer.exe restarts
        // kill the underlying COM sink.
        public StartMenuMonitor()
        {
            // Poller must exist before the launcher sink is wired up: its event can in
            // principle fire synchronously from Advise() and reach UpdateMenuEventHook.
            setupPoller();
            setupForegroundHook();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(WarmUp));
            // Hooked lazily by UpdateMenuEventHook once positioning is relevant, not for the process lifetime.
            _launcherVisibility = new ShellComProxy<LauncherVisibility>("StartMenuMonitor: launcher visibility",
                () =>
                {
                    var launcher = new LauncherVisibility();
                    launcher.Changed += OnLauncherVisibilityChanged;
                    return launcher;
                },
                stale =>
                {
                    stale.Changed -= OnLauncherVisibilityChanged;
                    stale.Dispose();
                });
            ExplorerMonitor.ExplorerRestarted += ExplorerMonitor_ExplorerRestarted;
        }

        // The launcher visibility proxy restarts itself; the immersive launcher cache is ours to drop.
        private void ExplorerMonitor_ExplorerRestarted(object sender, EventArgs e)
        {
            // Reset ImmersiveShellHelper's cached shell/factory first: GetImmersiveLauncher_* below
            // pulls the shell from there, so a fresh launcher must not be connected via a dead shell.
            ImmersiveShellHelper.Reset();

            // The cached IImmersiveLauncher/monitor RCWs point at the old explorer.exe process;
            // drop them so the next Start press re-queries fresh ones instead of failing with
            // RPC_E_DISCONNECTED/RPC_S_SERVER_UNAVAILABLE until our app itself restarts.
            InvalidateCachedLaunchers();
        }

        // COM sink callback, not necessarily on the UI thread; re-run the poll logic there without waiting for a tick.
        private void OnLauncherVisibilityChanged(object sender, EventArgs e)
        {
            _poller?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_poller?.IsEnabled == true) poller_Tick(this, EventArgs.Empty);
            }));
        }

        // Pure so it's testable by reflection without constructing a StartMenuMonitor.
        private static bool ShouldHookMenuEvents(bool hasPlacement, bool hasActivatedTaskbar) => hasPlacement || hasActivatedTaskbar;

        private void UpdateMenuEventHook()
        {
            bool shouldHook = ShouldHookMenuEvents(_correctPlacement != null, _placementTaskbar != IntPtr.Zero);
            if (shouldHook && _menuEventHook == null)
            {
                _menuEventHook = new WinEventHook("Start menu event hook", 0x8001, 0x800B, HandleMenuEvent);
            }
            else if (!shouldHook && _menuEventHook != null)
            {
                _menuEventHook.Dispose();
                _menuEventHook = null;
            }

            // Assigning Interval restarts the timer, so only on change.
            TimeSpan interval = (_isVisible || shouldHook) ? FastPollInterval : SlowPollInterval;
            if (_poller != null && _poller.Interval != interval) _poller.Interval = interval;
        }

        private void HandleMenuEvent(uint eventType, IntPtr hwnd, int idObject, int idChild)
        {
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0 || _correctingPlacement) return;
            if (eventType == 0x8001 || eventType == 0x8003)
            {
                if (hwnd == _positionedMenu && (eventType == 0x8001 || _positionedMenuShown))
                {
                    _fade.Restore(hwnd);
                    _avatarGuard.Disarm();
                    _positionedMenu = IntPtr.Zero;
                    _correctPlacement = null;
                    UpdateMenuEventHook();
                }
                return;
            }
            if (eventType != 0x8002 && eventType != 0x800B) return;
            if (eventType == 0x8002 && hwnd == _positionedMenu) _positionedMenuShown = true;
            if (_correctPlacement == null && _placementTaskbar == IntPtr.Zero) return;
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "OpenShell.CMenuContainer" && _positionedMenu == IntPtr.Zero)
                relocateStartMenu(hwnd);
            if (hwnd == _positionedMenu || name.ToString() == "OpenShell.CUserWindow")
                CorrectPlacement();
        }

        private void CorrectPlacement()
        {
            if (_correctingPlacement) return;
            _correctingPlacement = true;
            try { _correctPlacement?.Invoke(); }
            finally { _correctingPlacement = false; }
        }

        private void setupForegroundHook()
        {
            // R9-H: shared with the other UI-thread EVENT_SYSTEM_FOREGROUND subscribers via the hub
            // instead of each installing its own WinEventHook (review section 5).
            _foregroundSubscription = WinEventHub.Subscribe("Start menu foreground hook", EVENT_SYSTEM_FOREGROUND,
                (type, hwnd, obj, child) => HandleForeground(hwnd));
        }

        private void HandleForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !EnvironmentHelper.IsWindows8OrBetter)
            {
                return;
            }

            StringBuilder cName = new StringBuilder(256);
            GetClassName(hwnd, cName, cName.Capacity);
            string className = cName.ToString();

            // Modern Start (Win10/11) and Open Shell Menu both become foreground when they
            // open; classic Start (DV2ControlHost) does not reliably, so it stays on the
            // poller below.
            if (className == "Windows.UI.Core.CoreWindow")
            {
                // CoreWindow is shared by Search, Action Center, notification flyouts, the
                // emoji/input panel, etc. Only treat it as Start opening if we caused this
                // (Start button/ShowStartMenu already set _placementTaskbar) or the
                // immersive launcher itself reports visible (e.g. a bare Win-key press).
                if (_placementTaskbar == IntPtr.Zero && !isModernStartMenuOpen())
                {
                    return;
                }
            }
            else if (className != "OpenShell.CMenuContainer")
            {
                return;
            }

            relocateStartMenu(hwnd);
            setVisibility(true, MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));
        }

        private void setupPoller()
        {
            if (_poller == null)
            {
                _poller = new DispatcherTimer
                {
                    Interval = SlowPollInterval
                };

                _poller.Tick += poller_Tick;
            }

            _poller.Start();
        }

        private void poller_Tick(object sender, EventArgs e)
        {
            bool newIsVisible = false;
            IntPtr startHmonitor = IntPtr.Zero;

            if (EnvironmentHelper.IsWindows8OrBetter && isModernStartMenuOpen())
            {
                // Windows 8+
                newIsVisible = true;
                startHmonitor = relocateAndHMonitorModernStartMenu();
            }

            if (!newIsVisible && isClassicStartMenuOpen())
            {
                // Windows 7, StartIsBack, Start8+
                newIsVisible = true;
                startHmonitor = hMonitorClassicStartMenu();
            }

            if (!newIsVisible && isOpenShellMenuOpen())
            {
                // Open Shell Menu
                newIsVisible = true;
                relocateStartMenuByClass("OpenShell.CMenuContainer");
                startHmonitor = hMonitorOpenShellMenu();
            }

            // A placeholder not shown yet is still opening: dropping it here left the menu where Open Shell put it.
            bool opening = false;
            if (_positionedMenu != IntPtr.Zero && IsWindow(_positionedMenu) && !_positionedMenuShown)
            {
                if (IsWindowVisible(_positionedMenu) && !IsCloaked(_positionedMenu)) _positionedMenuShown = true;
                else opening = ++_openingTicks <= MaxOpeningTicks;
            }

            if (newIsVisible || !opening) setVisibility(newIsVisible, startHmonitor);

            // Safety net for a missed HIDE/DESTROY; the modern Start window is cloaked on close rather than hidden.
            if (_positionedMenu != IntPtr.Zero && !opening && (!IsWindow(_positionedMenu) || !IsWindowVisible(_positionedMenu) || IsCloaked(_positionedMenu)))
            {
                _fade.Restore(_positionedMenu);
                _avatarGuard.Disarm();
                _positionedMenu = IntPtr.Zero;
                _correctPlacement = null;
                UpdateMenuEventHook();
            }

            // Bounded fallback if ShowStartMenu activates a taskbar but no menu ever appears.
            if (_placementTaskbar == IntPtr.Zero || _isVisible)
            {
                _staleActivationTicks = 0;
            }
            else if (++_staleActivationTicks > 100)
            {
                _taskbarHwndActivated = IntPtr.Zero;
                _placementTaskbar = IntPtr.Zero;
                _staleActivationTicks = 0;
                UpdateMenuEventHook();
            }
        }

        private void setVisibility(bool isVisible, IntPtr startHmonitor)
        {
            if (isVisible == _isVisible)
            {
                return;
            }

            _isVisible = isVisible;

            StartMenuMonitorEventArgs args = new StartMenuMonitorEventArgs
            {
                Visible = _isVisible,
                TaskbarHwndActivated = _taskbarHwndActivated,
                StartHmonitor = startHmonitor
            };

            StartMenuVisibilityChanged?.Invoke(this, args);

            if (_taskbarHwndActivated != IntPtr.Zero)
            {
                // Now that it has been consumed, reset to prevent sending stale data
                // if the menu is opened again not by the start button.
                _taskbarHwndActivated = IntPtr.Zero;
            }
            // Placement still needs the pressed panel after the event consumed it; drop it only on close.
            if (!_isVisible) _placementTaskbar = IntPtr.Zero;

            UpdateMenuEventHook();
        }

        private bool isModernStartMenuOpen()
        {
            var launcher = _launcherVisibility.Get();
            if (launcher == null) return false;

            try
            {
                return launcher.IsVisible();
            }
            catch (Exception error) when (error is COMException || error is InvalidComObjectException)
            {
                ShellLogger.Warning($"StartMenuMonitor: IsLauncherVisible COM failed: {error.Message}");
                _launcherVisibility.Report(error);
                return false;
            }
        }

        private bool isClassicStartMenuOpen()
        {
            return isVisibleByClass("DV2ControlHost");
        }

        private bool isOpenShellMenuOpen()
        {
            return isVisibleByClass("OpenShell.CMenuContainer");
        }

        private bool isVisibleByClass(string className) => FindVisibleWindowByClass(className) != IntPtr.Zero;

        // Open Shell can keep several menu containers; the first in Z-order may be a hidden one.
        private static IntPtr FindVisibleWindowByClass(string className)
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, className, IntPtr.Zero)) != IntPtr.Zero)
            {
                if (IsWindowVisible(hwnd)) return hwnd;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _disposed = true;
            _poller?.Stop();
            _correctPlacement = null;
            _fade.Dispose();
            _avatarGuard.Dispose();
            ExplorerMonitor.ExplorerRestarted -= ExplorerMonitor_ExplorerRestarted;
            _launcherVisibility.Dispose();
            _menuEventHook?.Dispose();
            _menuEventHook = null;
            _foregroundSubscription?.Dispose();
            _foregroundSubscription = null;
        }

        public class StartMenuMonitorEventArgs : LauncherVisibilityEventArgs
        {
            public IntPtr TaskbarHwndActivated;
            public IntPtr StartHmonitor;
        }
    }
}
