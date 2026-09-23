using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using UltraWinBar.Utilities;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace UltraWinBar
{
    public partial class Taskbar
    {
        #region Unlocked taskbar drag hook
        private void Taskbar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsLocked)
            {
                // Start low-level mouse hook to receive current drag position
                // The hook should be stopped upon mouse up
                StartMouseDragHook();
            }
        }

        private AppBarEdge DragCoordsToScreenEdge(int x, int y)
        {
            // The areas of the screen which determine the dragged-to edge are divided in an X.
            // To determine the edge, split the screen into quadrants, and then split the quadrants diagonally, alternating.
            double relativeX = ((double)x - Screen.Bounds.Left) / Screen.Bounds.Width;
            double relativeY = ((double)y - Screen.Bounds.Top) / Screen.Bounds.Height;

            // We will use the relative coordinates to form quadrants
            // Determine the edge based on the quadrant

            if (relativeX < 0.5 && relativeY < 0.5)
            {
                // top-left quadrant
                if (relativeX >= relativeY)
                {
                    return AppBarEdge.Top;
                }
                else
                {
                    return AppBarEdge.Left;
                }
            }
            else if (relativeX >= 0.5 && relativeY < 0.5)
            {
                // top-right quadrant
                // adjust relativeX to the same base as relativeY
                relativeX -= 0.5;

                if (relativeX + relativeY < 0.5)
                {
                    return AppBarEdge.Top;
                }
                else
                {
                    return AppBarEdge.Right;
                }
            }
            else if (relativeX < 0.5 && relativeY >= 0.5)
            {
                // bottom-left quadrant
                // adjust relativeY to the same base as relativeX
                relativeY -= 0.5;

                if (relativeX + relativeY < 0.5)
                {
                    return AppBarEdge.Left;
                }
                else
                {
                    return AppBarEdge.Bottom;
                }
            }
            else
            {
                // bottom-right quadrant
                if (relativeX >= relativeY)
                {
                    return AppBarEdge.Right;
                }
                else
                {
                    return AppBarEdge.Bottom;
                }
            }
        }

        private void MouseDragHook_LowLevelMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            switch (e.Message)
            {
                case NativeMethods.WM.MOUSEMOVE:
                    if (_mouseDragStart == null)
                    {
                        return;
                    }

                    if (_mouseDragResize)
                    {
                        Dispatcher.BeginInvoke(() => {
                            int mouseX = e.HookStruct.pt.X;
                            int mouseY = e.HookStruct.pt.Y;
                            // Calculate where the resize edge should be, in case the actual resize operation is lagging behind the mouse
                            double scaledRowHeight = DesiredRowHeight * DpiScale;
                            if (Orientation == Orientation.Horizontal)
                            {
                                double taskbarEdge = AppBarEdge == AppBarEdge.Top ? Screen.Bounds.Top + (DesiredHeight * DpiScale) : Screen.Bounds.Bottom - (DesiredHeight * DpiScale);
                                if ((AppBarEdge == AppBarEdge.Top && mouseY < taskbarEdge - SystemParameters.MinimumVerticalDragDistance ||
                                     AppBarEdge == AppBarEdge.Bottom && mouseY > taskbarEdge + SystemParameters.MinimumVerticalDragDistance) &&
                                     Rows > 1)
                                {
                                    // If mouse is inside the taskbar and more than the minimum drag distance away, decrement size
                                    Rows -= 1;
                                }
                                else if ((AppBarEdge == AppBarEdge.Top && mouseY >= taskbarEdge + scaledRowHeight ||
                                          AppBarEdge == AppBarEdge.Bottom && mouseY <= taskbarEdge - scaledRowHeight) &&
                                          Rows < Settings.Instance.RowLimit)
                                {
                                    // If mouse is outside the taskbar and at least one row height away, increment size
                                    Rows += 1;
                                }
                            }
                            else
                            {
                                double taskbarEdge = AppBarEdge == AppBarEdge.Left ? Screen.Bounds.Left + (DesiredWidth * DpiScale) : Screen.Bounds.Right - (DesiredWidth * DpiScale);
                                if ((AppBarEdge == AppBarEdge.Left && mouseX > taskbarEdge + scaledRowHeight ||
                                     AppBarEdge == AppBarEdge.Right && mouseX < taskbarEdge - scaledRowHeight) &&
                                    TaskbarWidthCount < Settings.Instance.TaskbarWidthLimit)
                                {
                                    TaskbarWidthCount += 1;
                                }
                                else if ((AppBarEdge == AppBarEdge.Left && mouseX < taskbarEdge - SystemParameters.MinimumHorizontalDragDistance ||
                                          AppBarEdge == AppBarEdge.Right && mouseX > taskbarEdge + SystemParameters.MinimumHorizontalDragDistance) &&
                                    TaskbarWidthCount > 1)
                                {
                                    TaskbarWidthCount -= 1;
                                }
                            }
                        });
                        return;
                    }

                    if (Math.Abs(e.HookStruct.pt.X - (double)(_mouseDragStart?.X)) <= SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(e.HookStruct.pt.Y - (double)(_mouseDragStart?.Y)) <= SystemParameters.MinimumVerticalDragDistance)
                    {
                        return;
                    }

                    AppBarEdge newEdge = DragCoordsToScreenEdge(e.HookStruct.pt.X, e.HookStruct.pt.Y);
                    if (newEdge != AppBarEdge)
                    {
                        MoveToEdge(newEdge);
                    }
                    break;
                case NativeMethods.WM.LBUTTONUP:
                case NativeMethods.WM.LBUTTONDOWN:
                case NativeMethods.WM.MBUTTONUP:
                case NativeMethods.WM.MBUTTONDOWN:
                case NativeMethods.WM.RBUTTONUP:
                case NativeMethods.WM.RBUTTONDOWN:
                case NativeMethods.WM.XBUTTONUP:
                case NativeMethods.WM.XBUTTONDOWN:
                    StopMouseDragHook();
                    break;
            }
        }

        /// <summary>
        /// Moves this taskbar to another edge, updating whichever setting owns this
        /// taskbar's edge. Edges already occupied by another taskbar are rejected.
        /// </summary>
        private void MoveToEdge(AppBarEdge newEdge)
        {
            if (Settings.Instance.EnabledEdges.Contains(newEdge))
            {
                return;
            }

            if (IsPrimaryEdge)
            {
                Settings.Instance.Edge = newEdge;
                return;
            }

            var edges = new System.Collections.Generic.List<AppBarEdge>(Settings.Instance.AdditionalEdges);
            int index = edges.IndexOf(AppBarEdge);

            if (index >= 0)
            {
                edges[index] = newEdge;
            }
            else
            {
                edges.Add(newEdge);
            }

            // Assigning a new list instance notifies WindowManager, which reopens the taskbars.
            Settings.Instance.AdditionalEdges = edges;
        }

        private void StartMouseDragHook()
        {
            if (_mouseDragHook != null)
            {
                return;
            }

            _mouseDragHook = new LowLevelMouseHook();
            _mouseDragHook.LowLevelMouseEvent += MouseDragHook_LowLevelMouseEvent;
            if (!_mouseDragHook.Initialize())
            {
                ShellLogger.Warning("Mouse drag hook could not be initialized.");
                StopMouseDragHook();
                return;
            }
            _mouseDragStart = new Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
            _mouseDragResize = IsMouseInResizeArea();

            ShellLogger.Debug($"Mouse drag hook started");
        }

        private void StopMouseDragHook()
        {
            _mouseDragHook.LowLevelMouseEvent -= MouseDragHook_LowLevelMouseEvent;
            _mouseDragHook.Dispose();
            _mouseDragHook = null;
            _mouseDragStart = null;
            _mouseDragResize = false;

            ShellLogger.Debug("Mouse drag hook removed");
        }

        private bool IsMouseInResizeArea()
        {
            if (IsLocked) return false;

            int resizeRegionSize = (int)((_unlockedMargin > 0 ? _unlockedMargin : SystemParameters.MinimumVerticalDragDistance * Settings.Instance.TaskbarScale) * DpiScale);
            int mouseX = System.Windows.Forms.Cursor.Position.X;
            int mouseY = System.Windows.Forms.Cursor.Position.Y;

            if (AppBarEdge == AppBarEdge.Bottom && mouseY <= (int)(Top * DpiScale) + resizeRegionSize)
            {
                return true;
            }
            else if (AppBarEdge == AppBarEdge.Top && mouseY >= (int)((Top + Height) * DpiScale) - resizeRegionSize)
            {
                return true;
            }
            else if (AppBarEdge == AppBarEdge.Left && mouseX >= (int)((Left + Width) * DpiScale) - resizeRegionSize)
            {
                return true;
            }
            else if (AppBarEdge == AppBarEdge.Right && mouseX <= (int)(Left * DpiScale) + resizeRegionSize)
            {
                return true;
            }

            return false;
        }

        private void Taskbar_MouseMove(object sender, MouseEventArgs e)
        {
            // Show resize cursor for resizable taskbars
            if (IsMouseInResizeArea() || _mouseDragResize)
            {
                Cursor = Orientation == Orientation.Horizontal ? Cursors.SizeNS : Cursors.SizeWE;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }
        #endregion
    }
}
