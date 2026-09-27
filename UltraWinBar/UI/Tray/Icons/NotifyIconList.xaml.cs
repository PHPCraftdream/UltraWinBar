using GongSolutions.Wpf.DragDrop;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;
using UltraWinBar.Extensions;
using UltraWinBar.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Tray = ManagedShell.WindowsTray;

namespace UltraWinBar.Controls
{
    /// <summary>
    /// Interaction logic for NotifyIconList.xaml
    /// </summary>
    public partial class NotifyIconList : UserControl
    {
        private bool _isLoaded;
        private bool _isActive;
        private ObservableCollection<Tray.NotifyIcon> promotedIcons = new ObservableCollection<Tray.NotifyIcon>();
        private NotifyIconDropHandler dropHandler;
        private ListCollectionView collectionView;

        public static DependencyProperty NotificationAreaProperty = DependencyProperty.Register(nameof(NotificationArea), typeof(NotificationArea), typeof(NotifyIconList), new PropertyMetadata(NotificationAreaChangedCallback));

        public NotificationArea NotificationArea
        {
            get { return (NotificationArea)GetValue(NotificationAreaProperty); }
            set { SetValue(NotificationAreaProperty, value); }
        }

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(NotifyIconList), new PropertyMetadata(HostChangedCallback));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        public NotifyIconList()
        {
            InitializeComponent();
            IsVisibleChanged += NotifyIconList_OnIsVisibleChanged;
        }

        private bool IsCollapsed()
        {
            return Settings.Instance.CollapseNotifyIcons && NotifyIconToggleButton.IsChecked != true;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.CollapseNotifyIcons))
            {
                if (Settings.Instance.CollapseNotifyIcons)
                {
                    SetToggleVisibility();
                }
                else
                {
                    NotifyIconToggleButton.IsChecked = false;
                    NotifyIconToggleButton.Visibility = Visibility.Collapsed;
                }
                collectionView?.Refresh();
            }
            else if (e.PropertyName == nameof(Settings.InvertIconsMode) || e.PropertyName == nameof(Settings.InvertNotifyIcons) || e.PropertyName == nameof(Settings.NotifyIconOrder))
            {
                // Reload icons
                collectionView?.Refresh();
            }
        }

        private void SetNotificationAreaCollections()
        {
            if (!_isLoaded && NotificationArea != null)
            {
                NotificationArea.UnpinnedIcons.CollectionChanged += UnpinnedIcons_CollectionChanged;
                Predicate<object> unpinnedFilter = UnpinnedNotifyIcons_Filter;
                if (!Equals(NotificationArea.UnpinnedIcons.Filter, unpinnedFilter)) NotificationArea.UnpinnedIcons.Filter = unpinnedFilter;
                Settings.Instance.PropertyChanged += Settings_PropertyChanged;

                collectionView = new ListCollectionView(NotificationArea.TrayIcons);
                collectionView.CustomSort = new NotifyIconComparer(this);
                collectionView.Filter = NotifyIcons_Filter;
                var collectionViewShaping = collectionView as ICollectionViewLiveShaping;
                collectionViewShaping.IsLiveFiltering = true;
                collectionViewShaping.LiveFilteringProperties.Add("IsHidden");
                collectionViewShaping.LiveFilteringProperties.Add("IsPinned");
                NotifyIcons.ItemsSource = collectionView;

                if (Settings.Instance.CollapseNotifyIcons)
                {
                    SetToggleVisibility();
                }

                _isLoaded = true;
            }

            UpdateActivation();
        }

        private static void NotificationAreaChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is NotifyIconList notifyIconList && e.OldValue == null && e.NewValue != null)
            {
                notifyIconList.SetNotificationAreaCollections();
            }
        }

        private static void HostChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is NotifyIconList notifyIconList)
            {
                notifyIconList.UpdateActivation();
            }
        }

        private void NotifyIconList_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateActivation();
        }

        // Only visible, tray-hosting lists promote icons on balloons.
        private bool ShouldBeActive => NotificationArea != null && IsVisible && Host?.HostsTray == true;

        private void UpdateActivation()
        {
            if (ShouldBeActive)
            {
                Activate();
            }
            else
            {
                Deactivate();
            }
        }

        private void Activate()
        {
            if (_isActive)
            {
                return;
            }

            NotificationArea.NotificationBalloonShown += NotificationArea_NotificationBalloonShown;
            _isActive = true;
        }

        private void Deactivate()
        {
            if (!_isActive)
            {
                return;
            }

            NotificationArea.NotificationBalloonShown -= NotificationArea_NotificationBalloonShown;
            _isActive = false;
        }

        private bool NotifyIcons_Filter(object icon)
        {
            if (icon is Tray.NotifyIcon notifyIcon)
            {
                return (!IsCollapsed() || notifyIcon.IsPinned)
                    && !notifyIcon.IsHidden
                    && notifyIcon.GetBehavior() != NotifyIconBehavior.Remove;
            }
            return false;
        }

        // Static: the shared view must not retain whichever list set it last.
        private static bool UnpinnedNotifyIcons_Filter(object obj)
        {
            // This filter is used when we check if the toggle should hide
            if (obj is Tray.NotifyIcon notifyIcon)
            {
                return !notifyIcon.IsPinned && !notifyIcon.IsHidden && notifyIcon.GetBehavior() != NotifyIconBehavior.Remove;
            }

            return true;
        }

        private void NotificationArea_NotificationBalloonShown(object sender, NotificationBalloonEventArgs e)
        {
            // This is used to promote unpinned icons to show when the tray is collapsed.

            if (NotificationArea == null)
            {
                return;
            }

            Tray.NotifyIcon notifyIcon = e.Balloon.NotifyIcon;

            // ManagedShell appends unhandled balloons to MissedNotifications after this event.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => NotifyIcon.PruneMissedNotifications(notifyIcon)));

            if (NotificationArea.PinnedIcons.Contains(notifyIcon))
            {
                // Do not promote pinned icons (they're already there!)
                return;
            }

            if (notifyIcon.GetBehavior() != NotifyIconBehavior.HideWhenInactive)
            {
                // Do not promote icons that are always hidden
                return;
            }

            if (promotedIcons.Contains(notifyIcon))
            {
                // Do not duplicate promoted icons
                return;
            }

            notifyIcon.IsPinned = true;
            promotedIcons.Add(notifyIcon);

            DispatcherTimer unpromoteTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(e.Balloon.Timeout + 500) // Keep it around for a few ms for the animation to complete
            };
            unpromoteTimer.Tick += (object sender, EventArgs e) =>
            {
                if (promotedIcons.Contains(notifyIcon))
                {
                    if (!(Settings.Instance.NotifyIconBehaviors.Find(setting => setting.Identifier == notifyIcon.Identifier) is NotifyIconBehaviorSetting iconSetting && iconSetting.Behavior == NotifyIconBehavior.AlwaysShow))
                    {
                        // Don't unpin if settings were changed to always show
                        notifyIcon.IsPinned = false;
                    }
                    promotedIcons.Remove(notifyIcon);
                }
                unpromoteTimer.Stop();
            };
            unpromoteTimer.Start();
        }

        private void NotifyIconList_Loaded(object sender, RoutedEventArgs e)
        {
            SetNotificationAreaCollections();

            // Set up drag/drop handler
            if (dropHandler == null)
            {
                dropHandler = new NotifyIconDropHandler(this);
                GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(NotifyIcons, dropHandler);
            }
        }

        private void NotifyIconList_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            Deactivate();

            if (NotificationArea != null)
            {
                NotificationArea.UnpinnedIcons.CollectionChanged -= UnpinnedIcons_CollectionChanged;
                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            }

            // TrayIcons is app-lifetime: an attached view would keep this whole panel alive.
            NotifyIcons.ItemsSource = null;
            if (collectionView != null)
            {
                if (collectionView is ICollectionViewLiveShaping shaping)
                {
                    shaping.IsLiveFiltering = false;
                    shaping.LiveFilteringProperties.Clear();
                }
                collectionView.CustomSort = null;
                collectionView.Filter = null;
                collectionView.DetachFromSourceCollection();
                collectionView = null;
            }

            _isLoaded = false;
        }

        private void UnpinnedIcons_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SetToggleVisibility();
        }

        private void NotifyIconToggleButton_OnClick(object sender, RoutedEventArgs e)
        {
            collectionView?.Refresh();
        }

        private void SetToggleVisibility()
        {
            if (!Settings.Instance.CollapseNotifyIcons) return;

            if (NotificationArea.UnpinnedIcons.IsEmpty)
            {
                NotifyIconToggleButton.Visibility = Visibility.Collapsed;

                if (NotifyIconToggleButton.IsChecked == true)
                {
                    NotifyIconToggleButton.IsChecked = false;
                }
            }
            else
            {
                NotifyIconToggleButton.Visibility = Visibility.Visible;
            }
        }

        private void TrayArea_OnMouseEnter(object sender, MouseEventArgs e)
        {
            SweepDeadIcons();
        }

        // Icons whose owner process died without sending NIM_DELETE stay in TrayIcons forever
        // (ManagedShell only self-heals via mouse-hover on that specific icon, which never
        // happens for one we've hidden). Sweep on tray-area hover, removing through the same
        // ObservableCollection ManagedShell itself uses so its internal state stays consistent.
        private void SweepDeadIcons()
        {
            if (NotificationArea == null)
            {
                return;
            }

            foreach (Tray.NotifyIcon icon in NotificationArea.TrayIcons.ToList())
            {
                if (icon.HWnd == IntPtr.Zero || !NativeMethods.IsWindow(icon.HWnd))
                {
                    NotificationArea.TrayIcons.Remove(icon);
                }
            }
        }

        public void UpdateIconOrder(IDropInfo dropInfo)
        {
            if (NotificationArea == null || collectionView == null) return;

            var visibleIcons = collectionView.Cast<Tray.NotifyIcon>().ToList();

            if (IsCollapsed())
            {
                // Do not save temporary promoted icons
                visibleIcons = visibleIcons.Where(i => !promotedIcons.Contains(i)).ToList();
            }

            // Update the dragged icon's position in the list
            if (dropInfo.Data is Tray.NotifyIcon draggedIcon)
            {
                int insertIndex = dropInfo.InsertIndex;
                if (insertIndex > 0 && visibleIcons.IndexOf(draggedIcon) < insertIndex && visibleIcons.Remove(draggedIcon))
                {
                    insertIndex--;
                }
                else
                {
                    visibleIcons.Remove(draggedIcon);
                }
                visibleIcons.Insert(insertIndex, draggedIcon);
            }
            else
            {
                return;
            }
            
            // Never overwrite the list to prevent clearing out settings for non-visible icons
            var oldOrder = Settings.Instance.NotifyIconOrder ?? new List<string>();
            var result = new List<string>();
            int replaceIndex = 0;
            
            foreach (var id in oldOrder)
            {
                if (visibleIcons.Find(i => i.IsEqualByIdentifier(id)) != null)
                {
                    if (replaceIndex < visibleIcons.Count)
                    {
                        result.Add(visibleIcons[replaceIndex++].Identifier);
                    }
                }
                else
                {
                    result.Add(id);
                }
            }

            while (replaceIndex < visibleIcons.Count)
            {
                result.Add(visibleIcons[replaceIndex++].Identifier);
            }

            Settings.Instance.NotifyIconOrder = result;
        }

        public class NotifyIconComparer : System.Collections.IComparer
        {
            private NotifyIconList _host;

            public NotifyIconComparer(NotifyIconList host)
            {
                _host = host;
            }

            public int Compare(object x, object y)
            {
                if (x is Tray.NotifyIcon xIcon && y is Tray.NotifyIcon yIcon && Settings.Instance.NotifyIconOrder is List<string> setting)
                {
                    if (_host.IsCollapsed())
                    {
                        bool xPromoted = _host.promotedIcons.Contains(xIcon);
                        bool yPromoted = _host.promotedIcons.Contains(yIcon);
                        if (xPromoted && !yPromoted)
                        {
                            return -1;
                        }
                        if (!xPromoted && yPromoted)
                        {
                            return 1;
                        }
                    }
                    int xIndex = setting.FindIndex(s => xIcon.IsEqualByIdentifier(s));
                    int yIndex = setting.FindIndex(s => yIcon.IsEqualByIdentifier(s));
                    return xIndex.CompareTo(yIndex);
                }
                return 0;
            }
        }
    }
}