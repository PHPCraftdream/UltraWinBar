using ManagedShell.AppBar;
using ManagedShell.WindowsTasks;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using UltraWinBar.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace UltraWinBar.Controls
{
    /// <summary>
    /// Interaction logic for TaskList.xaml
    /// </summary>
    public partial class TaskList : UserControl
    {
        private bool isLoaded;
        private bool isScrollable;
        private double DefaultButtonWidth;
        private double MinButtonWidth;
        private double TaskButtonLeftMargin;
        private double TaskButtonRightMargin;
        private ICollectionView taskbarItems;
        private INotifyCollectionChanged sourceWindows;
        private readonly HashSet<ApplicationWindow> observedWindows = new(ReferenceEqualityComparer.Instance);
        private bool viewRefreshPending;

        public static DependencyProperty ButtonWidthProperty = DependencyProperty.Register(nameof(ButtonWidth), typeof(double), typeof(TaskList), new PropertyMetadata(new double()));

        public double ButtonWidth
        {
            get { return (double)GetValue(ButtonWidthProperty); }
            set { SetValue(ButtonWidthProperty, value); }
        }

        public static DependencyProperty ButtonsPerRowProperty = DependencyProperty.Register(nameof(ButtonsPerRow), typeof(int), typeof(TaskList), new PropertyMetadata(0));

        public int ButtonsPerRow
        {
            get { return (int)GetValue(ButtonsPerRowProperty); }
            set { SetValue(ButtonsPerRowProperty, value); }
        }

        // Number of columns in a full row that get one extra pixel, so the row fills the
        // taskbar exactly instead of leaving the floored remainder empty.
        public static DependencyProperty ExtraWidthCountProperty = DependencyProperty.Register(nameof(ExtraWidthCount), typeof(int), typeof(TaskList), new PropertyMetadata(0));

        public int ExtraWidthCount
        {
            get { return (int)GetValue(ExtraWidthCountProperty); }
            set { SetValue(ExtraWidthCountProperty, value); }
        }

        public static DependencyProperty TasksProperty = DependencyProperty.Register(nameof(Tasks), typeof(Tasks), typeof(TaskList), new PropertyMetadata(TasksChangedCallback));

        public Tasks Tasks
        {
            get { return (Tasks)GetValue(TasksProperty); }
            set { SetValue(TasksProperty, value); }
        }

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(TaskList), new PropertyMetadata(TasksChangedCallback));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        public TaskList()
        {
            InitializeComponent();
        }

        // The edge of the taskbar hosting this list, which may differ from the primary edge.
        internal AppBarEdge HostEdge => Host?.AppBarEdge ?? Settings.Instance.Edge;

        private void SetStyles()
        {
            DefaultButtonWidth = Application.Current.FindResource("TaskButtonWidth") as double? ?? 0;
            MinButtonWidth = Application.Current.FindResource("TaskButtonMinWidth") as double? ?? 0;
            Thickness buttonMargin;

            if (HostEdge == AppBarEdge.Left || HostEdge == AppBarEdge.Right)
            {
                buttonMargin = Application.Current.FindResource("TaskButtonVerticalMargin") as Thickness? ?? new Thickness();
            }
            else
            {
                buttonMargin = Application.Current.FindResource("TaskButtonMargin") as Thickness? ?? new Thickness();
            }

            TaskButtonLeftMargin = buttonMargin.Left;
            TaskButtonRightMargin = buttonMargin.Right;
        }

        private void TaskList_OnLoaded(object sender, RoutedEventArgs e)
        {
            SetStyles();
            SetTasksCollection();
        }

        private void SetTasksCollection()
        {
            if (!isLoaded && Tasks != null && Host != null)
            {
                var source = Tasks.GroupedWindows.SourceCollection as IList
                    ?? throw new InvalidOperationException("Task window source is not a list.");
                taskbarItems = CreateWindowView(source, Tasks_Filter);
                if (taskbarItems != null)
                {
                    taskbarItems.CollectionChanged += GroupedWindows_CollectionChanged;
                }

                sourceWindows = source as INotifyCollectionChanged;
                if (sourceWindows != null) sourceWindows.CollectionChanged += SourceWindows_CollectionChanged;
                foreach (var window in source.OfType<ApplicationWindow>()) WatchWindow(window);

                TasksList.ItemsSource = displayedTasks;

                Settings.Instance.PropertyChanged += Settings_PropertyChanged;
                Host.hotkeyManager.TaskbarHotkeyPressed += TaskList_TaskbarHotkeyPressed;

                isLoaded = true;
                if (VirtualDesktopContext.Instance != null) VirtualDesktopContext.Instance.Changed += DesktopChanged;
                QueueTaskRebuild();
            }
        }

        internal static ICollectionView CreateWindowView(IList source, Predicate<object> filter) =>
            new ListCollectionView(source) { Filter = filter };

        private static void TasksChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TaskList taskList && e.OldValue == null && e.NewValue != null)
            {
                taskList.SetTasksCollection();
            }
        }

        private void DesktopChanged(object sender, EventArgs e)
        {
            RefreshWindowVisibility();
        }

        internal void RefreshWindowVisibility()
        {
            QueueViewRefresh();
        }

        private void QueueViewRefresh()
        {
            if (viewRefreshPending) return;
            viewRefreshPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                viewRefreshPending = false;
                if (!isLoaded) return;
                taskbarItems?.Refresh();
                QueueTaskRebuild();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SourceWindows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var window in observedWindows.ToArray()) UnwatchWindow(window);
                foreach (var window in Tasks.GroupedWindows.SourceCollection.Cast<object>().OfType<ApplicationWindow>())
                    WatchWindow(window);
            }
            else
            {
                if (e.OldItems != null)
                    foreach (ApplicationWindow window in e.OldItems) UnwatchWindow(window);
                if (e.NewItems != null)
                    foreach (ApplicationWindow window in e.NewItems) WatchWindow(window);
            }
            QueueViewRefresh();
        }

        private void WatchWindow(ApplicationWindow window)
        {
            if (observedWindows.Add(window)) window.PropertyChanged += Window_PropertyChanged;
        }

        private void UnwatchWindow(ApplicationWindow window)
        {
            if (observedWindows.Remove(window)) window.PropertyChanged -= Window_PropertyChanged;
        }

        private void Window_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ApplicationWindow.ShowInTaskbar) or nameof(ApplicationWindow.HMonitor) or null or "")
                QueueViewRefresh();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.PinnedApplications)) QueueTaskRebuild();
            if (e.PropertyName == nameof(Settings.MultiMonMode) ||
                e.PropertyName == nameof(Settings.TaskbarAssignments) ||
                e.PropertyName == nameof(Settings.AdditionalEdges) ||
                e.PropertyName == nameof(Settings.Edge) ||
                e.PropertyName == nameof(Settings.DefaultTaskEdge))
            {
                QueueViewRefresh();
            }
            else if (e.PropertyName == nameof(Settings.ShowMultiMon))
            {
                if (Settings.Instance.MultiMonMode != MultiMonOption.AllTaskbars)
                {
                    QueueViewRefresh();
                }
            }
        }
        private void TaskList_TaskbarHotkeyPressed(object sender, HotkeyManager.TaskbarHotkeyEventArgs e)
        {
            if (Settings.Instance.WinNumHotkeysAction == WinNumHotkeysOption.SwitchTasks && Host.Screen.Primary)
            {
                try
                {
                    bool exists = e.index >= 0 && e.index < displayedTasks.Count;

                    if (exists)
                    {
                        if (displayedTasks[e.index] is PinnedApplication pin)
                        {
                            pin.Launch(Tasks);
                            return;
                        }
                        ApplicationWindow window = displayedTasks[e.index] as ApplicationWindow;
                        if (window == null) return;

                        if (e.isShiftPressed)
                        {
                            // Open new instance when Shift is pressed
                            ShellHelper.StartProcess(window.IsUWP ? "appx:" + window.AppUserModelID : window.WinFileName);
                        }
                        else
                        {
                            // Normal behavior - switch to existing window
                            if (window.State == ApplicationWindow.WindowState.Active && window.CanMinimize)
                            {
                                window.Minimize();
                            }
                            else
                            {
                                window.BringToFront();
                            }
                        }
                    }

                }
                catch (ArgumentOutOfRangeException) { }
            }
        }

        private bool Tasks_Filter(object obj)
        {
            if (obj is ApplicationWindow window)
            {
                if (VirtualDesktopContext.Instance?.IsOnCurrentDesktop(window.Handle) == false) return false;
                if (!window.ShowInTaskbar)
                {
                    return false;
                }

                if (!IsOnAssignedEdge(window))
                {
                    return false;
                }

                if (!Settings.Instance.ShowMultiMon || Settings.Instance.MultiMonMode == MultiMonOption.AllTaskbars)
                {
                    return true;
                }

                if (Settings.Instance.MultiMonMode == MultiMonOption.SameAsWindowAndPrimary && Host.Screen.Primary)
                {
                    return true;
                }

                IntPtr hMonitor = window.HMonitor;
                if (Host.Screen.Primary && !Host.windowManager.IsValidHMonitor(hMonitor))
                {
                    return true;
                }

                if (hMonitor != Host.Screen.HMonitor)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether this taskbar is the one this window's application belongs to. Windows
        /// with no assignment stick to the primary-edge taskbar, matching the single-taskbar
        /// behavior from before per-application assignments existed.
        /// </summary>
        private bool IsOnAssignedEdge(ApplicationWindow window)
        {
            if (Settings.Instance.EnabledEdges.Count < 2)
            {
                return true;
            }

            AppBarEdge targetEdge = TaskAssignmentManager.GetAssignedEdge(window) ?? Settings.Instance.ResolvedDefaultTaskEdge;
            return HostEdge == targetEdge;
        }

        private void TaskList_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (VirtualDesktopContext.Instance != null) VirtualDesktopContext.Instance.Changed -= DesktopChanged;
            if (sourceWindows != null) sourceWindows.CollectionChanged -= SourceWindows_CollectionChanged;
            foreach (var window in observedWindows.ToArray()) UnwatchWindow(window);
            sourceWindows = null;
            if (taskbarItems != null)
            {
                taskbarItems.CollectionChanged -= GroupedWindows_CollectionChanged;
                taskbarItems.Filter = null;
            }

            if (Host != null)
            {
                Host.hotkeyManager.TaskbarHotkeyPressed -= TaskList_TaskbarHotkeyPressed;
            }

            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

            isLoaded = false;
        }

        private void GroupedWindows_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            QueueTaskRebuild();
        }


        internal void ShowTaskInsertionIndicator(Point screenPoint)
        {
            try
            {
                if (!TryGetTaskInsertionTarget(screenPoint, out TaskInsertionTarget target))
                {
                    HideTaskInsertionIndicator();
                    return;
                }

                Point topLeft = InsertionOverlay.PointFromScreen(target.ScreenTopLeft);
                Point bottomRight = InsertionOverlay.PointFromScreen(target.ScreenBottomRight);
                double left = Math.Min(topLeft.X, bottomRight.X);
                double top = Math.Min(topLeft.Y, bottomRight.Y);
                double right = Math.Max(topLeft.X, bottomRight.X);
                double bottom = Math.Max(topLeft.Y, bottomRight.Y);
                const double thickness = 2;

                if (IsVertical)
                {
                    InsertionIndicator.Width = Math.Max(thickness, right - left);
                    InsertionIndicator.Height = thickness;
                    Canvas.SetLeft(InsertionIndicator, left);
                    Canvas.SetTop(InsertionIndicator, target.Before ? top : Math.Max(top, bottom - thickness));
                }
                else
                {
                    bool leadingIsLeft = TasksList.FlowDirection != FlowDirection.RightToLeft;
                    bool useLeftEdge = target.Before == leadingIsLeft;

                    InsertionIndicator.Width = thickness;
                    InsertionIndicator.Height = Math.Max(thickness, bottom - top);
                    Canvas.SetLeft(InsertionIndicator, useLeftEdge ? left : Math.Max(left, right - thickness));
                    Canvas.SetTop(InsertionIndicator, top);
                }

                InsertionIndicator.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                HideTaskInsertionIndicator();
                ShellLogger.Error($"Task drag indicator failed on {HostEdge}: {ex}");
            }
        }

        internal void HideTaskInsertionIndicator()
        {
            InsertionIndicator.Visibility = Visibility.Collapsed;
        }

        private bool IsVertical => HostEdge == AppBarEdge.Left || HostEdge == AppBarEdge.Right;

        private bool TryGetTaskInsertionTarget(Point screenPoint, out TaskInsertionTarget target)
        {
            target = null;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < TasksList.Items.Count; i++)
            {
                TaskButton container = GetTaskButtonContainer(i);
                if (container == null ||
                    container.ActualWidth <= 0 || container.ActualHeight <= 0)
                {
                    continue;
                }

                Point firstCorner = container.PointToScreen(new Point(0, 0));
                Point secondCorner = container.PointToScreen(new Point(container.ActualWidth, container.ActualHeight));
                double left = Math.Min(firstCorner.X, secondCorner.X);
                double top = Math.Min(firstCorner.Y, secondCorner.Y);
                double right = Math.Max(firstCorner.X, secondCorner.X);
                double bottom = Math.Max(firstCorner.Y, secondCorner.Y);
                double dx = screenPoint.X < left ? left - screenPoint.X : screenPoint.X > right ? screenPoint.X - right : 0;
                double dy = screenPoint.Y < top ? top - screenPoint.Y : screenPoint.Y > bottom ? screenPoint.Y - bottom : 0;
                double distance = dx * dx + dy * dy;

                if (distance >= bestDistance)
                {
                    continue;
                }

                bool before;
                if (IsVertical)
                {
                    before = screenPoint.Y < (top + bottom) / 2;
                }
                else if (TasksList.FlowDirection == FlowDirection.RightToLeft)
                {
                    before = screenPoint.X > (left + right) / 2;
                }
                else
                {
                    before = screenPoint.X < (left + right) / 2;
                }

                bestDistance = distance;
                target = new TaskInsertionTarget
                {
                    InsertIndex = i + (before ? 0 : 1),
                    Before = before,
                    ScreenTopLeft = new Point(left, top),
                    ScreenBottomRight = new Point(right, bottom)
                };
            }

            return target != null;
        }

        private TaskButton GetTaskButtonContainer(int index)
        {
            DependencyObject container = TasksList.ItemContainerGenerator.ContainerFromIndex(index);
            return container as TaskButton ?? FindVisualChild<TaskButton>(container);
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                T descendant = FindVisualChild<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private sealed class TaskInsertionTarget
        {
            public int InsertIndex { get; set; }
            public bool Before { get; set; }
            public Point ScreenTopLeft { get; set; }
            public Point ScreenBottomRight { get; set; }
        }

        private void TaskList_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetTaskButtonWidth();
        }

        private void SetTaskButtonWidth()
        {
            if (Host is null)
                return; // The state is trashed, but presumably it's just a transition

            if (HostEdge == AppBarEdge.Left || HostEdge == AppBarEdge.Right)
            {
                ExtraWidthCount = 0;
                ButtonWidth = ActualWidth;
                SetScrollable(true); // while technically not always scrollable, we don't run into DPI-specific issues with it enabled while vertical
                return;
            }

            double height = ActualHeight;
            int rows = Host.Rows;

            int taskCount = TasksList.Items.Count;
            TasksList.AlternationCount = taskCount; // keep in sync for correct AlternationIndex

            double margin = TaskButtonLeftMargin + TaskButtonRightMargin;
            ButtonsPerRow = Math.Max(1, (int)Math.Ceiling((double)taskCount / rows));
            double maxWidth = TasksList.ActualWidth / ButtonsPerRow;
            double defaultWidth = DefaultButtonWidth + margin;
            double minWidth = MinButtonWidth + margin;

            if (maxWidth > defaultWidth)
            {
                // Room to spare: keep the default width and leave the trailing gap.
                ExtraWidthCount = 0;
                ButtonWidth = defaultWidth;
                SetScrollable(false);
            }
            else if (maxWidth < minWidth)
            {
                ExtraWidthCount = 0;
                ButtonWidth = Math.Ceiling(defaultWidth / 2);
                SetScrollable(true);
            }
            else
            {
                // Buttons are shrunk to fit a full row: spread the pixels lost to flooring
                // across the columns so the row fills the taskbar.
                double baseWidth = Math.Floor(maxWidth);
                ButtonWidth = baseWidth;
                ExtraWidthCount = (int)Math.Floor(TasksList.ActualWidth) - (int)baseWidth * ButtonsPerRow;
                SetScrollable(false);
            }
        }

        private void SetScrollable(bool canScroll)
        {
            if (canScroll == isScrollable) return;

            if (canScroll)
            {
                TasksScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            else
            {
                TasksScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }

            isScrollable = canScroll;
        }

        private void TasksScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (!isScrollable && Settings.Instance.TaskWheelAction == TaskWheelActionOption.DoNothing)
            {
                e.Handled = true;
            }
        }
    }
}
