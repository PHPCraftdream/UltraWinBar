using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
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
    public partial class StartMenuMonitor
    {
        private IntPtr relocateAndHMonitorModernStartMenu()
        {
            IntPtr hwndForeground = GetForegroundWindow();
            StringBuilder cName = new StringBuilder(256);
            GetClassName(hwndForeground, cName, cName.Capacity);
            if (cName.ToString() == "Windows.UI.Core.CoreWindow")
            {
                // When the modern Start menu opens, it gains focus, so this is probably it.
                // Unlike Open Shell, the OS doesn't know where our custom Start button is, so
                // it opens wherever it assumes the (hidden) system taskbar's button is. Correct
                // that the same way relocateStartMenuByClass does for Open Shell.
                relocateStartMenu(hwndForeground);
                return MonitorFromWindow(hwndForeground, MONITOR_DEFAULTTONEAREST);
            }
            return IntPtr.Zero;
        }

        private IntPtr hMonitorClassicStartMenu()
        {
            return hMonitorByClass("DV2ControlHost");
        }

        private IntPtr hMonitorOpenShellMenu()
        {
            return hMonitorByClass("OpenShell.CMenuContainer");
        }

        private IntPtr hMonitorByClass(string className)
        {
            IntPtr hStartMenu = FindVisibleWindowByClass(className);

            if (hStartMenu == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return MonitorFromWindow(hStartMenu, MONITOR_DEFAULTTONEAREST);
        }

        private void relocateStartMenuByClass(string className)
        {
            IntPtr hStartMenu = FindVisibleWindowByClass(className);
            if (hStartMenu == IntPtr.Zero)
            {
                return;
            }

            relocateStartMenu(hStartMenu);
        }

        // Menus we did not open (Win key, a lost activation race) are anchored too, never left where Windows put them.
        private static IntPtr FindAnchorTaskbar(IntPtr hStartMenu)
        {
            var bars = Application.Current.Windows.OfType<UltraWinBar.Taskbar>()
                .Where(bar => !bar.IsClosing && bar.Handle != IntPtr.Zero).ToList();
            int index = StartMenuPlacement.ChooseAnchor(bars
                .Select(bar => (bar.Screen.HMonitor, bar.HostsStartButton && bar.StartButton.Visibility == Visibility.Visible, bar.Screen.Primary))
                .ToList(), MonitorFromWindow(hStartMenu, MONITOR_DEFAULTTONEAREST));
            return index < 0 ? IntPtr.Zero : bars[index].Handle;
        }

        private void relocateStartMenu(IntPtr hStartMenu)
        {
            IntPtr anchor = _placementTaskbar != IntPtr.Zero ? _placementTaskbar : FindAnchorTaskbar(hStartMenu);
            if (_positionedMenu == hStartMenu && _correctPlacement != null && _positionedTaskbar == anchor)
            {
                CorrectPlacement();
                return;
            }
            if (anchor == IntPtr.Zero)
            {
                return;
            }

            FlowDirection flowDirection = Application.Current.FindResource("flow_direction") as FlowDirection? ?? FlowDirection.LeftToRight;
            GetWindowRect(hStartMenu, out ManagedShell.Interop.NativeMethods.Rect startMenuRect);
            GetWindowRect(anchor, out ManagedShell.Interop.NativeMethods.Rect taskbarRect);
            ShellLogger.Debug($"StartMenuMonitor: relocateStartMenu entered, hStartMenu={hStartMenu}, anchor={anchor} (pressed={_placementTaskbar != IntPtr.Zero}), currentRect=({startMenuRect.Left},{startMenuRect.Top},{startMenuRect.Right},{startMenuRect.Bottom}), taskbarRect=({taskbarRect.Left},{taskbarRect.Top},{taskbarRect.Right},{taskbarRect.Bottom})");

            // Use the edge of whichever taskbar the button was actually pressed on, not
            // the primary one — with multiple taskbars they can differ.
            AppBarEdge edge = Settings.Instance.Edge;
            foreach (UltraWinBar.Taskbar taskbar in Application.Current.Windows.OfType<UltraWinBar.Taskbar>())
            {
                if (taskbar.Handle == anchor)
                {
                    edge = taskbar.AppBarEdge;
                    break;
                }
            }

            var screen = System.Windows.Forms.Screen.FromHandle(anchor);
            var area = WindowPlacementGuard.AvailableArea(screen.Bounds);
            Point initialTarget = StartMenuPlacement.GetTarget(startMenuRect, taskbarRect, area, edge, flowDirection == FlowDirection.RightToLeft);
            int x = (int)initialTarget.X, y = (int)initialTarget.Y;

            bool menuAlreadyAtTarget = y == startMenuRect.Top && x == startMenuRect.Left;
            if (!menuAlreadyAtTarget && startMenuRect.Width > 200 && startMenuRect.Height > 200)
            {
                bool moved = SetWindowPos(hStartMenu, IntPtr.Zero, x, y, 0, 0, (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                ShellLogger.Debug($"StartMenuMonitor: SetWindowPos target=({x},{y}) returned {moved}");
            }

            // Open Shell first exposes a 101x100 placeholder, then creates its full menu and
            // the separate user-picture HWND. Capture their original relationship once both
            // exist, then keep applying absolute targets so retries cannot accumulate drift.
            ManagedShell.Interop.NativeMethods.Rect referenceMenuRect = startMenuRect;
            bool hasReferenceMenuRect = startMenuRect.Width > 200 && startMenuRect.Height > 200;
            bool hasUserPictureOffset = false;
            int userPictureOffsetX = 0;
            int userPictureOffsetY = 0;
            _positionedMenu = hStartMenu;
            _positionedTaskbar = anchor;
            _positionedMenuShown = IsWindowVisible(hStartMenu) && !IsCloaked(hStartMenu);
            _openingTicks = 0;
            _correctPlacement = () =>
            {
                if (!IsWindow(hStartMenu))
                {
                    _correctPlacement = null;
                    _positionedMenu = IntPtr.Zero;
                    UpdateMenuEventHook();
                    return;
                }

                GetWindowRect(hStartMenu, out ManagedShell.Interop.NativeMethods.Rect currentRect);
                if (currentRect.Width <= 200 || currentRect.Height <= 200) return;
                Point target = StartMenuPlacement.GetTarget(currentRect, taskbarRect,
                    WindowPlacementGuard.AvailableArea(screen.Bounds), edge, flowDirection == FlowDirection.RightToLeft);
                x = (int)target.X;
                y = (int)target.Y;
                if ((currentRect.Left != x || currentRect.Top != y) &&
                    currentRect.Width > 200 && currentRect.Height > 200)
                {
                    referenceMenuRect = currentRect;
                    hasReferenceMenuRect = true;
                }

                if (currentRect.Left != x || currentRect.Top != y)
                {
                    SetWindowPos(hStartMenu, IntPtr.Zero, x, y, 0, 0, (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                }

                IntPtr hUserPicture = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "OpenShell.CUserWindow", IntPtr.Zero);
                if (hUserPicture == IntPtr.Zero)
                {
                    hUserPicture = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Desktop User Picture", IntPtr.Zero);
                }
                if (!hasUserPictureOffset && hasReferenceMenuRect && hUserPicture != IntPtr.Zero)
                {
                    GetWindowRect(hUserPicture, out ManagedShell.Interop.NativeMethods.Rect userPictureRect);
                    bool userPictureIsPositioned = userPictureRect.Left >= referenceMenuRect.Left &&
                        userPictureRect.Left < referenceMenuRect.Right &&
                        userPictureRect.Top >= referenceMenuRect.Top &&
                        userPictureRect.Top < referenceMenuRect.Bottom;
                    if (userPictureIsPositioned)
                    {
                        userPictureOffsetX = userPictureRect.Left - referenceMenuRect.Left;
                        userPictureOffsetY = userPictureRect.Top - referenceMenuRect.Top;
                        hasUserPictureOffset = true;
                        ShellLogger.Debug($"StartMenuMonitor: captured user-picture offset=({userPictureOffsetX},{userPictureOffsetY})");
                    }
                }

                if (hasUserPictureOffset && hUserPicture != IntPtr.Zero)
                {
                    GetWindowRect(hUserPicture, out var pictureRect);
                    if (pictureRect.Left != x + userPictureOffsetX || pictureRect.Top != y + userPictureOffsetY)
                        SetWindowPos(hUserPicture, IntPtr.Zero, x + userPictureOffsetX, y + userPictureOffsetY, 0, 0,
                            (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                }

            };
            UpdateMenuEventHook();
            CorrectPlacement();
        }
    }
}
