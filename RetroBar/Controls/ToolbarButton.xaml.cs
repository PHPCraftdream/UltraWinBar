using ManagedShell.ShellFolders;
using RetroBar.Utilities;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace RetroBar.Controls
{
    /// <summary>
    /// Interaction logic for ToolbarButton.xaml
    /// </summary>
    public partial class ToolbarButton : UserControl
    {
        private LowLevelMouseHook _dragHook;
        private LowLevelMouseHook.POINT _dragStartScreenPos;
        private bool _isDraggingToTaskbar;

        public ToolbarButton()
        {
            InitializeComponent();

            setIconBinding();
        }

        private void setIconBinding()
        {
            string bindingPath = "SmallIcon";
            bool useLargeIcons = Settings.Instance.TaskbarScale > 1 || (Application.Current.FindResource("UseLargeIcons") as bool? ?? false);

            if (useLargeIcons)
            {
                bindingPath = "LargeIcon";
            }

            Binding iconBinding = new Binding(bindingPath);
            iconBinding.Mode = BindingMode.OneWay;
            ToolbarIcon.SetBinding(Image.SourceProperty, iconBinding);
        }

        // Ctrl+drag moves this shortcut to another taskbar (Settings.QuickLaunchAssignments).
        // A plain drag still reorders within this panel via gong-wpf-dragdrop, untouched —
        // gating on Ctrl here means we never compete with it for the same mouse gesture.
        #region Cross-taskbar drag (Ctrl+drag)
        private void ToolbarButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Settings.Instance.EnabledEdges.Count < 2)
            {
                return;
            }

            e.Handled = true;

            if (_dragHook != null)
            {
                return;
            }

            _dragStartScreenPos = new LowLevelMouseHook.POINT
            {
                X = System.Windows.Forms.Cursor.Position.X,
                Y = System.Windows.Forms.Cursor.Position.Y
            };
            _isDraggingToTaskbar = false;

            _dragHook = new LowLevelMouseHook();
            _dragHook.LowLevelMouseEvent += DragHook_LowLevelMouseEvent;
            _dragHook.Initialize();
        }

        private void DragHook_LowLevelMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            switch (e.Message)
            {
                case ManagedShell.Interop.NativeMethods.WM.MOUSEMOVE:
                    if (!_isDraggingToTaskbar)
                    {
                        if (System.Math.Abs(e.HookStruct.pt.X - _dragStartScreenPos.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                            System.Math.Abs(e.HookStruct.pt.Y - _dragStartScreenPos.Y) <= SystemParameters.MinimumVerticalDragDistance)
                        {
                            return;
                        }

                        _isDraggingToTaskbar = true;
                        Dispatcher.BeginInvoke(() => Mouse.Capture(null));
                    }

                    Dispatcher.BeginInvoke(() =>
                    {
                        Cursor = FindTaskbarAtScreenPoint(e.HookStruct.pt) != null ? Cursors.Hand : Cursors.No;
                    });
                    break;
                case ManagedShell.Interop.NativeMethods.WM.LBUTTONUP:
                    bool wasDragging = _isDraggingToTaskbar;
                    LowLevelMouseHook.MSLLHOOKSTRUCT hookStruct = e.HookStruct;
                    StopDragHook();

                    if (wasDragging)
                    {
                        Dispatcher.BeginInvoke(() => CompleteDragToTaskbar(hookStruct.pt));
                    }
                    break;
                case ManagedShell.Interop.NativeMethods.WM.RBUTTONUP:
                case ManagedShell.Interop.NativeMethods.WM.MBUTTONUP:
                case ManagedShell.Interop.NativeMethods.WM.XBUTTONUP:
                    StopDragHook();
                    break;
            }
        }

        private static Taskbar FindTaskbarAtScreenPoint(LowLevelMouseHook.POINT pt)
        {
            foreach (Taskbar taskbar in Application.Current.Windows.OfType<Taskbar>())
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

        private void CompleteDragToTaskbar(LowLevelMouseHook.POINT pt)
        {
            Cursor = Cursors.Arrow;

            Taskbar targetTaskbar = FindTaskbarAtScreenPoint(pt);
            if (targetTaskbar == null)
            {
                return;
            }

            if (DataContext is ShellFile file)
            {
                Settings.Instance.SetQuickLaunchEdge(file.Path, targetTaskbar.AppBarEdge);
            }
        }

        private void StopDragHook()
        {
            if (_dragHook == null)
            {
                return;
            }

            _dragHook.LowLevelMouseEvent -= DragHook_LowLevelMouseEvent;
            _dragHook.Dispose();
            _dragHook = null;
            _isDraggingToTaskbar = false;
        }
        #endregion
    }
}
