using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using RetroBar.Converters;
using RetroBar.Utilities;

namespace RetroBar.Controls
{
    /// <summary>
    /// Interaction logic for TaskButton.xaml
    /// </summary>
    public partial class TaskButton : UserControl
    {
        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(TaskList), typeof(TaskButton));

        public TaskList Host
        {
            get { return (TaskList)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        private ApplicationWindow Window;
        private TaskButtonStyleConverter StyleConverter = new TaskButtonStyleConverter();
        private ApplicationWindow.WindowState PressedWindowState = ApplicationWindow.WindowState.Inactive;

        private DelayedActivationHandler dragHandler;
        private bool _isLoaded;

        private bool _isTaskDragArmed;
        private System.Drawing.Point _dragStartScreenPos;
        private bool _isDraggingToTaskbar;
        private TaskList _dragFeedbackTaskList;

        public TaskButton()
        {
            InitializeComponent();
            SetStyle();
        }

        private void SetStyle()
        {
            MultiBinding multiBinding = new MultiBinding();
            multiBinding.Converter = StyleConverter;

            multiBinding.Bindings.Add(new Binding { RelativeSource = RelativeSource.Self });
            multiBinding.Bindings.Add(new Binding("State"));

            AppButton.SetBinding(StyleProperty, multiBinding);
        }

        private void ScrollIntoView()
        {
            if (Window == null)
            {
                return;
            }

            if (Window.State == ApplicationWindow.WindowState.Active)
            {
                BringIntoView();
            }
        }

        private void Animate()
        {
            var ease = new SineEase();
            ease.EasingMode = EasingMode.EaseInOut;

            DoubleAnimation animation = new DoubleAnimation();
            animation.From = 0;
            animation.To = Host?.ButtonWidth ?? ActualWidth;
            animation.Duration = new Duration(TimeSpan.FromMilliseconds(250));
            animation.FillBehavior = FillBehavior.Stop;
            animation.EasingFunction = ease;
            Storyboard.SetTarget(animation, this);
            Storyboard.SetTargetProperty(animation, new PropertyPath(WidthProperty));

            Storyboard storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private void TaskButton_OnLoaded(object sender, RoutedEventArgs e)
        {
            Window = DataContext as ApplicationWindow;

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            dragHandler = new DelayedActivationHandler(() =>
            {
                Window?.BringToFront();
            });

            if (Window != null)
            {
                Window.GetButtonRect += Window_GetButtonRect;
                Window.PropertyChanged += Window_PropertyChanged;
            }

            if (Settings.Instance.SlideTaskbarButtons && Host?.Host?.Orientation == Orientation.Horizontal)
            {
                Animate();
            }

            _isLoaded = true;
        }

        private void Window_GetButtonRect(ref NativeMethods.ShortRect rect)
        {
            if (Host?.Host?.Screen.Primary != true && Settings.Instance.MultiMonMode != MultiMonOption.SameAsWindow)
            {
                // If there are multiple instances of a button, use the button on the primary display only
                return;
            }

            Point buttonTopLeft = PointToScreen(new Point(0, 0));
            Point buttonBottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            rect.Top = (short)buttonTopLeft.Y;
            rect.Left = (short)buttonTopLeft.X;
            rect.Bottom = (short)buttonBottomRight.Y;
            rect.Right = (short)buttonBottomRight.X;
        }

        private void Window_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "State")
            {
                ScrollIntoView();
            }
        }

        private void TaskButton_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            dragHandler?.Dispose();
            CancelTaskDrag();

            if (Window != null)
            {
                Window.GetButtonRect -= Window_GetButtonRect;
                Window.PropertyChanged -= Window_PropertyChanged;
            }

            _isLoaded = false;
        }

        private void AppButton_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (Window == null)
            {
                return;
            }

            NativeMethods.WindowShowStyle wss = Window.ShowStyle;
            int ws = Window.WindowStyles;

            // disable window operations depending on current window state. originally tried implementing via bindings but found there is no notification we get regarding maximized state
            MaximizeMenuItem.IsEnabled = wss != NativeMethods.WindowShowStyle.ShowMaximized && (ws & (int)NativeMethods.WindowStyles.WS_MAXIMIZEBOX) != 0;
            MinimizeMenuItem.IsEnabled = wss != NativeMethods.WindowShowStyle.ShowMinimized && Window.CanMinimize;
            if (RestoreMenuItem.IsEnabled = wss != NativeMethods.WindowShowStyle.ShowNormal)
            {
                CloseMenuItem.FontWeight = FontWeights.Normal;
                RestoreMenuItem.FontWeight = FontWeights.Bold;
            }
            if (!RestoreMenuItem.IsEnabled || RestoreMenuItem.IsEnabled && !MaximizeMenuItem.IsEnabled)
            {
                CloseMenuItem.FontWeight = FontWeights.Bold;
                RestoreMenuItem.FontWeight = FontWeights.Normal;
            }
            MoveMenuItem.IsEnabled = wss == NativeMethods.WindowShowStyle.ShowNormal;
            SizeMenuItem.IsEnabled = wss == NativeMethods.WindowShowStyle.ShowNormal && (ws & (int)NativeMethods.WindowStyles.WS_MAXIMIZEBOX) != 0;

            ResetTaskAssignmentMenuItem.Visibility = Settings.Instance.EnabledEdges.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetTaskAssignmentMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (Window != null)
            {
                TaskAssignmentManager.ResetAssignment(Window);
            }
        }

        private void CloseMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Close();
        }

        private void EndTaskMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (Window != null)
            {
                ForceEndTask();
            }
        }

        private void ForceEndTask()
        {
            try
            {
                if (Window.ProcId.HasValue && Window.ProcId.Value != 0)
                {
                    // Don't kill RetroBar itself - just close the window gracefully
                    int currentProcId = Process.GetCurrentProcess().Id;
                    if (Window.ProcId.Value == currentProcId)
                    {
                        Window?.Close();
                        return;
                    }

                    Process process = Process.GetProcessById((int)Window.ProcId.Value);
                    process.Kill();
                }
            }
            catch (Exception)
            {
                Window?.Close();
            }
        }

        private void RestoreMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Restore();
        }

        private void MoveMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Move();
        }

        private void SizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Size();
        }

        private void MinimizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Minimize();
        }

        private void MaximizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Maximize();
        }

        private void AppButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (PressedWindowState == ApplicationWindow.WindowState.Active && Window?.CanMinimize == true)
            {
                Window?.Minimize();
            }
            else
            {
                Window?.BringToFront();
            }
        }

        private void AppButton_OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Settings.Instance.TaskWheelAction == TaskWheelActionOption.DoNothing || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                return;
            }

            if (e.Delta > 0)
            {
                // SetForegroundWindow can fail if the process doesn't have foreground rights
                // We need to trigger a keyboard event for Windows to grant us this ability
                // The key code of 0xE8 is unassigned, so no apps should handle it
                var input = new NativeMethods.INPUT[]
                {
                    new() { type = NativeMethods.INPUT_KEYBOARD, mkhi = new() { ki = new() { wVk = 0xE8 } } },
                    new() { type = NativeMethods.INPUT_KEYBOARD, mkhi = new() { ki = new() { wVk = 0xE8, dwFlags = NativeMethods.KEYEVENTF_KEYUP } } }
                };
                NativeMethods.SendInput((uint)input.Length, input, Marshal.SizeOf<NativeMethods.INPUT>());
                Window?.BringToFront();
            }
            else if (Window?.CanMinimize == true)
            {
                Window?.Minimize();
            }
            e.Handled = true;
        }

        private void AppButton_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                PressedWindowState = Window.State;
                _dragStartScreenPos = System.Windows.Forms.Cursor.Position;
                _isTaskDragArmed = true;
                _isDraggingToTaskbar = false;
                bool captured = Mouse.Capture(AppButton, CaptureMode.Element);
                ShellLogger.Debug($"Task drag: down at {_dragStartScreenPos.X},{_dragStartScreenPos.Y}; captured={captured}");
            }
        }

        #region Task drag
        private void AppButton_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isTaskDragArmed)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelTaskDrag();
                return;
            }

            System.Drawing.Point screenPoint = System.Windows.Forms.Cursor.Position;
            if (!_isDraggingToTaskbar)
            {
                if (Math.Abs(screenPoint.X - _dragStartScreenPos.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(screenPoint.Y - _dragStartScreenPos.Y) <= SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _isDraggingToTaskbar = true;
                ShellLogger.Debug($"Task drag: threshold at {screenPoint.X},{screenPoint.Y}");
            }

            UpdateTaskDragFeedback(screenPoint);
            e.Handled = true;
        }

        private void AppButton_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isTaskDragArmed)
            {
                return;
            }

            bool wasDragging = _isDraggingToTaskbar;
            System.Drawing.Point screenPoint = System.Windows.Forms.Cursor.Position;
            ShellLogger.Debug($"Task drag: up at {screenPoint.X},{screenPoint.Y}; dragging={wasDragging}");
            _isTaskDragArmed = false;
            _isDraggingToTaskbar = false;
            ClearTaskDragFeedback();

            if (!wasDragging)
            {
                return;
            }

            e.Handled = true;
            AppButton.ReleaseMouseCapture();
            CompleteDragToTaskbar(screenPoint);
        }

        private Taskbar FindTaskbarAtScreenPoint(System.Drawing.Point pt)
        {
            foreach (Taskbar taskbar in System.Windows.Application.Current.Windows.OfType<Taskbar>())
            {
                double scale = taskbar.DpiScale;
                double left = taskbar.Left * scale;
                double top = taskbar.Top * scale;
                double right = left + (taskbar.ActualWidth * scale);
                double bottom = top + (taskbar.ActualHeight * scale);

                if (pt.X >= left && pt.X < right && pt.Y >= top && pt.Y < bottom)
                {
                    return taskbar;
                }
            }

            return null;
        }

        private void UpdateTaskDragFeedback(System.Drawing.Point screenPoint)
        {
            Taskbar targetTaskbar = FindTaskbarAtScreenPoint(screenPoint);
            TaskList targetTaskList = targetTaskbar?.FindName("TaskListControl") as TaskList;

            if (!ReferenceEquals(_dragFeedbackTaskList, targetTaskList))
            {
                _dragFeedbackTaskList?.HideTaskInsertionIndicator();
                _dragFeedbackTaskList = targetTaskList;
                ShellLogger.Debug($"Task drag: feedback target={targetTaskbar?.AppBarEdge.ToString() ?? "none"}");
            }

            if (targetTaskList == null)
            {
                Mouse.OverrideCursor = Cursors.No;
                return;
            }

            targetTaskList.ShowTaskInsertionIndicator(new Point(screenPoint.X, screenPoint.Y));
            Mouse.OverrideCursor = Cursors.Hand;
        }

        private void ClearTaskDragFeedback()
        {
            _dragFeedbackTaskList?.HideTaskInsertionIndicator();
            _dragFeedbackTaskList = null;
            Mouse.OverrideCursor = null;
        }

        private void CompleteDragToTaskbar(System.Drawing.Point pt)
        {
            Taskbar targetTaskbar = FindTaskbarAtScreenPoint(pt);
            ShellLogger.Debug($"Task drag: drop target={targetTaskbar?.AppBarEdge.ToString() ?? "none"} at {pt.X},{pt.Y}");

            if (targetTaskbar == null || Window == null)
            {
                return;
            }

            AppBarEdge targetEdge = targetTaskbar.AppBarEdge;
            AppBarEdge currentEdge = TaskAssignmentManager.GetAssignedEdge(Window) ?? Settings.Instance.ResolvedDefaultTaskEdge;

            if (targetEdge == currentEdge)
            {
                // Same panel — reorder in place instead of a no-op.
                if (targetTaskbar.FindName("TaskListControl") is TaskList taskList)
                {
                    taskList.ReorderTask(Window, new Point(pt.X, pt.Y));
                }
                return;
            }

            // Plain drag moves the whole application; holding Ctrl moves just this window.
            TaskAssignmentMode mode = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? TaskAssignmentMode.WindowClassAndTitle
                : TaskAssignmentMode.ExecutablePath;

            TaskAssignmentManager.AssignToEdge(Window, targetEdge, mode);
        }

        private void CancelTaskDrag()
        {
            _isTaskDragArmed = false;
            _isDraggingToTaskbar = false;
            ClearTaskDragFeedback();

            if (Mouse.Captured == AppButton)
            {
                AppButton.ReleaseMouseCapture();
            }
        }
        #endregion

        private void AppButton_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (Window == null || Settings.Instance.TaskMiddleClickAction == TaskMiddleClickOption.DoNothing)
                {
                    return;
                }
                if (Settings.Instance.TaskMiddleClickAction == TaskMiddleClickOption.CloseTask !=
                    (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
                {
                    Window?.Close();
                }
                else
                {
                    ShellHelper.StartProcess(Window.IsUWP ? "appx:" + Window.AppUserModelID : Window.WinFileName);
                }
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme))
            {
                SetStyle();
            }
        }

        private void AppButton_OnDragEnter(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragEnter(e);
        }

        private void AppButton_OnDragLeave(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragLeave();
        }

        private void ContextMenu_OpenedOrClosed(object sender, RoutedEventArgs e)
        {
            BindingOperations.GetMultiBindingExpression(AppButton, StyleProperty).UpdateTarget();
        }
    }
}
