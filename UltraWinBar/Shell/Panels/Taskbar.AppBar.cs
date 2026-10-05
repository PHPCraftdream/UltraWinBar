using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;
using UltraWinBar.Utilities;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace UltraWinBar
{
    public partial class Taskbar
    {
        #region AppBarWindow overrides
        protected override void OnSourceInitialized(object sender, EventArgs e)
        {
            base.OnSourceInitialized(sender, e);

            SetLayoutRounding();
            SetBlur(AllowsBlur());
            UpdateTrayPosition();
        }
        
        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == PanelReservation.CallbackMessage)
            {
                // Another AppBar moved: republish our rect so the shell keeps reserving it.
                if (_reserved && (int)wParam == PanelReservation.AbnPosChanged && _standaloneBounds.HasValue && !AllowClose)
                    PanelReservation.Reserve(Handle, AppBarEdge, _standaloneBounds.Value, true);
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGING &&
                windowManager?.UsesManualWorkArea == true && _standaloneBounds.HasValue && !AllowClose)
            {
                var proposed = NativeMethods.WINDOWPOS.FromMessage(lParam);
                var expected = _standaloneBounds.Value;
                bool moving = (proposed.flags & NativeMethods.SetWindowPosFlags.SWP_NOMOVE) == 0;
                bool sizing = (proposed.flags & NativeMethods.SetWindowPosFlags.SWP_NOSIZE) == 0;
                if ((moving && (proposed.x != expected.Left || proposed.y != expected.Top)) ||
                    (sizing && (proposed.cx != expected.Width || proposed.cy != expected.Height)))
                {
                    proposed.x = expected.Left;
                    proposed.y = expected.Top;
                    proposed.cx = expected.Width;
                    proposed.cy = expected.Height;
                    proposed.flags &= ~(NativeMethods.SetWindowPosFlags.SWP_NOMOVE | NativeMethods.SetWindowPosFlags.SWP_NOSIZE);
                    proposed.UpdateMessage(lParam);
                }
            }

            base.WndProc(hwnd, msg, wParam, lParam, ref handled);

            if (msg == (int)NativeMethods.WM.SETTINGCHANGE && wParam == (IntPtr)NativeMethods.SPI.SETWORKAREA)
            {
                windowManager?.NotifyWorkAreaChange();
                return IntPtr.Zero;
            }

            if ((msg == (int)NativeMethods.WM.SYSCOLORCHANGE ||
                    msg == (int)NativeMethods.WM.SETTINGCHANGE) &&
                Settings.Instance.Theme.StartsWith(DictionaryManager.THEME_DEFAULT))
            {
                handled = true;

                if (IsRelevantThemeTrigger(msg, wParam, lParam))
                {
                    // Color scheme changed: re-apply the theme once per broadcast burst, not per panel.
                    _dictionaryManager.RequestThemeReload();
                }
            }
            return IntPtr.Zero;
        }

        // Only broadcasts that can change system theme colors.
        private static bool IsRelevantThemeTrigger(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == (int)NativeMethods.WM.SYSCOLORCHANGE)
            {
                return true;
            }

            if (wParam == (IntPtr)NativeMethods.SPI.SETHIGHCONTRAST ||
                wParam == (IntPtr)NativeMethods.SPI.SETNONCLIENTMETRICS)
            {
                return true;
            }

            if (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
            {
                return true;
            }

            return false;
        }

        protected override void CustomClosing()
        {
            if (AllowClose)
            {
                if (_reserved)
                {
                    PanelReservation.Release(Handle);
                    _reserved = false;
                }
                QuickLaunchToolbar.Visibility = Visibility.Collapsed;

                WeakSubscriptions.UnsubscribeSettings(Settings_PropertyChanged);
                _startMenuMonitor.StartMenuVisibilityChanged -= StartMenuMonitor_StartMenuVisibilityChanged;
                _shellManager.TasksService.WindowActivated -= TasksService_WindowActivated;
                StopElementDragHook();
                // A drag to another edge reopens panels mid-gesture; never leave this panel's hook behind.
                StopMouseDragHook();
            }
        }

        protected override void SetScreenProperties(ScreenSetupReason reason)
        {
            if (reason == ScreenSetupReason.DpiChange)
            {
                // DPI change is per-monitor, update ourselves
                if (windowManager?.UsesManualWorkArea == true)
                    windowManager.RefreshManualLayout();
                else
                    UpdatePosition();
                SetLayoutRounding();
                return;
            }

            if (windowManager?.UsesManualWorkArea == true)
            {
                windowManager.NotifyDisplayChange(reason);
                return;
            }

            if (Settings.Instance.ShowMultiMon)
            {
                // Re-create UltraWinBar windows based on new screen setup
                windowManager.NotifyDisplayChange(reason);
            }
            else
            {
                // Update window as necessary
                base.SetScreenProperties(reason);
            }
        }

        protected override bool ShouldAllowAutoHide()
        {
            return (!_startMenuOpen || !Screen.Primary) && _openMenus < 1 && base.ShouldAllowAutoHide();
        }

        protected override void OnAutoHideAnimationBegin(bool isHiding)
        {
            base.OnAutoHideAnimationBegin(isHiding);

            // Prevent focus indicators and tooltips while hidden
            ResetControlFocus();

            if (!isHiding && Opacity < 1)
            {
                Opacity = 1;
                OnPropertyChanged(nameof(Opacity));
            }
        }

        protected override void OnAutoHideAnimationComplete(bool isHiding)
        {
            base.OnAutoHideAnimationComplete(isHiding);

            if (isHiding && Settings.Instance.AutoHideTransparent && AllowsTransparency && AllowAutoHide)
            {
                Opacity = 0.01;
                OnPropertyChanged(nameof(Opacity));
            }
        }

        protected override void OnFullScreenEnter(FullScreenApp app)
        {
            base.OnFullScreenEnter(app);
            StartButton?.UpdateFloatingStartTopmost(false);
        }

        protected override void OnFullScreenLeave()
        {
            base.OnFullScreenLeave();
            StartButton?.UpdateFloatingStartTopmost(true);
        }
        #endregion
    }
}
