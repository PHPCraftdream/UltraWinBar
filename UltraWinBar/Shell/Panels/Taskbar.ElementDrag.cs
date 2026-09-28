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
        #region Clock/tray drag between taskbars
        private LowLevelMouseHook _elementDragHook;
        private LowLevelMouseHook.POINT _elementDragStartScreenPos;
        private bool _isDraggingElement;
        private Action<AppBarEdge> _elementDragTarget;
        private string _elementDragGlyph;

        private void ClockGroupBox_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartElementDragHook(edge => Settings.Instance.ClockEdge = edge, "◷");
        }

        private void TrayGroupBox_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartElementDragHook(edge => Settings.Instance.TrayEdge = edge, "▦");
        }

        private void InputLanguageGroup_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartElementDragHook(edge => Settings.Instance.LanguageEdge = edge, "A");
        }

        // Observe the threshold without consuming ordinary clicks.
        private void StartElementDragHook(Action<AppBarEdge> onDroppedOnEdge, string glyph)
        {
            if (Settings.Instance.EnabledEdges.Count < 2 || _elementDragHook != null)
            {
                return;
            }

            _elementDragTarget = onDroppedOnEdge;
            ShellLogger.Debug($"Panel element drag: armed on {AppBarEdge}, glyph={glyph}");
            _elementDragGlyph = glyph;
            _elementDragStartScreenPos = new LowLevelMouseHook.POINT
            {
                X = System.Windows.Forms.Cursor.Position.X,
                Y = System.Windows.Forms.Cursor.Position.Y
            };
            _isDraggingElement = false;

            _elementDragHook = new LowLevelMouseHook();
            _elementDragHook.LowLevelMouseEvent += ElementDragHook_LowLevelMouseEvent;
            if (!_elementDragHook.Initialize()) StopElementDragHook();
        }

        private void ElementDragHook_LowLevelMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            switch (e.Message)
            {
                case NativeMethods.WM.MOUSEMOVE:
                    if (!_isDraggingElement)
                    {
                        if (Math.Abs(e.HookStruct.pt.X - _elementDragStartScreenPos.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                            Math.Abs(e.HookStruct.pt.Y - _elementDragStartScreenPos.Y) <= SystemParameters.MinimumVerticalDragDistance)
                        {
                            return;
                        }

                        _isDraggingElement = true;
                        var apply = _elementDragTarget;
                        var glyph = _elementDragGlyph;
                        StopElementDragHook();
                        Dispatcher.BeginInvoke(() =>
                        {
                            ShellLogger.Debug($"Panel element drag: threshold reached, buttons={System.Windows.Forms.Control.MouseButtons}");
                            if ((System.Windows.Forms.Control.MouseButtons & System.Windows.Forms.MouseButtons.Left) != 0 && IsVisible)
                                PanelElementDrag.Run(this, glyph, apply);
                        });
                    }
                    break;
                case NativeMethods.WM.LBUTTONUP:
                case NativeMethods.WM.RBUTTONUP:
                case NativeMethods.WM.MBUTTONUP:
                case NativeMethods.WM.XBUTTONUP:
                    StopElementDragHook();
                    break;
            }
        }

        private void StopElementDragHook()
        {
            if (_elementDragHook == null)
            {
                return;
            }

            _elementDragHook.LowLevelMouseEvent -= ElementDragHook_LowLevelMouseEvent;
            _elementDragHook.Dispose();
            _elementDragHook = null;
            _isDraggingElement = false;
            _elementDragTarget = null;
        }
        #endregion
    }
}
