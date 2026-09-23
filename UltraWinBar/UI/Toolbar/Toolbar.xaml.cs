using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.ShellFolders;
using ManagedShell.ShellFolders.Enums;
using UltraWinBar.Utilities;

namespace UltraWinBar.Controls
{
    /// <summary>
    /// Interaction logic for Toolbar.xaml
    /// </summary>
    public partial class Toolbar : UserControl
    {
        private bool _ignoreNextUpdate;
        private bool _isLoaded;

        private enum MenuItem : uint
        {
            OpenParentFolder = CommonContextMenuItem.Paste + 1,
            // 4 consecutive UIDs, one per AppBarEdge value (Left/Top/Right/Bottom).
            MoveToEdgeBase = CommonContextMenuItem.Paste + 2
        }

        public static DependencyProperty PathProperty = DependencyProperty.Register(nameof(Path), typeof(string), typeof(Toolbar), new PropertyMetadata(OnPathChanged));

        public string Path
        {
            get => (string)GetValue(PathProperty);
            set
            {
                SetValue(PathProperty, value);
                SetupFolder(value);
            }
        }

        private static DependencyProperty FolderProperty = DependencyProperty.Register(nameof(Folder), typeof(ShellFolder), typeof(Toolbar));

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(Toolbar), new PropertyMetadata(HostChangedCallback));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        public ToolbarDropHandler DropHandler { get; set; }

        private ShellFolder Folder
        {
            get => (ShellFolder)GetValue(FolderProperty);
            set
            {
                SetValue(FolderProperty, value);
                SetItemsSource();
            }
        }

        public Toolbar()
        {
            DropHandler = new ToolbarDropHandler(this);

            InitializeComponent();
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.QuickLaunchOrder))
            {
                if (_ignoreNextUpdate)
                {
                    _ignoreNextUpdate = false;
                    return;
                }

                Refresh();
            }
            else if (e.PropertyName == nameof(Settings.TaskbarScale))
            {
                Refresh();
            }
            else if (e.PropertyName == nameof(Settings.QuickLaunchAssignments) ||
                     e.PropertyName == nameof(Settings.DefaultTaskEdge) ||
                     e.PropertyName == nameof(Settings.AdditionalEdges))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            if (Folder == null)
            {
                return;
            }

            ListCollectionView cvs = (ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files);
            cvs.Refresh();
        }

        private void SetupFolder(string path)
        {
            Folder?.Dispose();
            Folder = new ShellFolder(Environment.ExpandEnvironmentVariables(path), IntPtr.Zero, true);
        }

        private void UnloadFolder()
        {
            Folder?.Dispose();
            Folder = null;
        }

        private void SetItemsSource()
        {
            if (Folder != null)
            {
                ToolbarItems.ItemsSource = Folder.Files;
                ListCollectionView cvs = (ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files);
                cvs.CustomSort = new ToolbarSorter(this);
                cvs.Filter = IsOwnQuickLaunchItem;
            }
        }

        private bool IsOwnQuickLaunchItem(object item)
        {
            return Host != null && item is ShellFile file &&
                   Settings.Instance.GetQuickLaunchEdge(file.Path) == Host.AppBarEdge;
        }

        public void SaveItemOrder()
        {
            List<string> visiblePaths = new List<string>();

            foreach (ShellFile file in ((ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files)).OfType<ShellFile>())
            {
                visiblePaths.Add(file.Path);
            }

            // The view is filtered to this panel's own items, so only reorder those —
            // append the other panels' items afterward, preserving their relative order,
            // instead of dropping them from the shared QuickLaunchOrder list entirely.
            HashSet<string> visibleSet = new HashSet<string>(visiblePaths);
            List<string> mergedOrder = new List<string>(visiblePaths);

            foreach (string existingPath in Settings.Instance.QuickLaunchOrder)
            {
                if (!visibleSet.Contains(existingPath))
                {
                    mergedOrder.Add(existingPath);
                }
            }

            // small optimization, only other toolbars with this folder need to reload when the setting is saved.
            _ignoreNextUpdate = true;

            Settings.Instance.QuickLaunchOrder = mergedOrder;
        }

        // Reorders a shortcut within this panel by nearest-neighbor screen position, driven by
        // ToolbarButton's own LowLevelMouseHook drag (gong-wpf-dragdrop was removed from this
        // ItemsControl — same reliability issue this session already hit with TaskButton/TaskList).
        public void ReorderQuickLaunchItem(string draggedPath, Point screenPoint)
        {
            if (Folder == null)
            {
                return;
            }

            List<ShellFile> visibleFiles = new List<ShellFile>();
            foreach (object item in (ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files))
            {
                if (item is ShellFile file)
                {
                    visibleFiles.Add(file);
                }
            }

            bool vertical = Host != null &&
                (Host.AppBarEdge == ManagedShell.AppBar.AppBarEdge.Left || Host.AppBarEdge == ManagedShell.AppBar.AppBarEdge.Right);

            int insertIndex = visibleFiles.Count;
            for (int i = 0; i < ToolbarItems.Items.Count; i++)
            {
                if (!(ToolbarItems.ItemContainerGenerator.ContainerFromIndex(i) is ToolbarButton container))
                {
                    continue;
                }

                Point topLeft = container.PointToScreen(new Point(0, 0));
                Point center = new Point(topLeft.X + container.ActualWidth / 2, topLeft.Y + container.ActualHeight / 2);

                if (vertical ? screenPoint.Y < center.Y : screenPoint.X < center.X)
                {
                    insertIndex = i;
                    break;
                }
            }

            List<string> newOrder = new List<string>();
            for (int i = 0; i < visibleFiles.Count; i++)
            {
                if (i == insertIndex)
                {
                    newOrder.Add(draggedPath);
                }

                if (visibleFiles[i].Path != draggedPath)
                {
                    newOrder.Add(visibleFiles[i].Path);
                }
            }

            if (insertIndex >= visibleFiles.Count)
            {
                newOrder.Add(draggedPath);
            }

            HashSet<string> visibleSet = new HashSet<string>();
            foreach (ShellFile file in visibleFiles)
            {
                visibleSet.Add(file.Path);
            }

            foreach (string existingPath in Settings.Instance.QuickLaunchOrder)
            {
                if (!visibleSet.Contains(existingPath) && existingPath != draggedPath)
                {
                    newOrder.Add(existingPath);
                }
            }

            _ignoreNextUpdate = true;
            Settings.Instance.QuickLaunchOrder = newOrder;
        }

        public void AddToSource(StringCollection filesToAdd)
        {
            string sourcePath = Environment.ExpandEnvironmentVariables(Path);

            foreach (string itemPath in filesToAdd)
            {
                // Create shortcut to each dragged file
                try
                {
                    string destinationFileName = System.IO.Path.GetFileNameWithoutExtension(itemPath);
                    string destinationPath = System.IO.Path.Combine(sourcePath, destinationFileName + ".lnk");
                    int dupCount = 0;

                    while (ShellHelper.Exists(destinationPath))
                    {
                        dupCount++;

                        destinationPath = System.IO.Path.Combine(sourcePath, $"{destinationFileName} ({dupCount}).lnk");
                    }

                    ShellLinkHelper.CreateAndSave(itemPath, destinationPath);
                }
                catch (Exception e)
                {
                    ShellLogger.Error($"Toolbar: Unable to save shortcut to {itemPath}", e);
                }
            }
        }

        #region Events
        private static void OnPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Toolbar toolbar)
            {
                toolbar.SetupFolder((string)e.NewValue);
            }
        }

        private void ToolbarIcon_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ToolbarButton icon = sender as ToolbarButton;
            if (icon == null)
            {
                return;
            }

            Mouse.Capture(null);
            ShellFile file = icon.DataContext as ShellFile;

            if (file == null || string.IsNullOrWhiteSpace(file.Path))
            {
                return;
            }

            if (InvokeContextMenu(file, false))
            {
                e.Handled = true;
            }
        }

        private void ToolbarIcon_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ToolbarButton icon = sender as ToolbarButton;
            if (icon == null)
            {
                return;
            }
            
            ShellFile file = icon.DataContext as ShellFile;

            if (InvokeContextMenu(file, true))
            {
                e.Handled = true;
            }
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible)
            {
                if (visible)
                {
                    if (Folder != null)
                    {
                        return;
                    }

                    SetupFolder(Path);
                }
                else
                {
                    UnloadFolder();
                }
            }
        }

        private void Toolbar_TaskbarHotkeyPressed(object sender, HotkeyManager.TaskbarHotkeyEventArgs e)
        {
            if (Settings.Instance.WinNumHotkeysAction == WinNumHotkeysOption.InvokeQuickLaunch && Host.Screen.Primary)
            {
                try
                {
                    ListCollectionView items = (ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files);

                    bool exists = items.MoveCurrentToPosition(e.index);
                    
                    if (exists) InvokeContextMenu((ShellFile)items.CurrentItem, false);

                }
                catch (ArgumentOutOfRangeException) { }
            }
        }
        #endregion

        #region Context menu
        private ShellMenuCommandBuilder GetFileCommandBuilder(ShellFile file)
        {
            if (file == null)
            {
                return new ShellMenuCommandBuilder();
            }

            ShellMenuCommandBuilder builder = new ShellMenuCommandBuilder();

            builder.AddSeparator();
            builder.AddCommand(new ShellMenuCommand
            {
                Flags = MFT.BYCOMMAND,
                Label = (string)FindResource("open_folder"),
                UID = (uint)MenuItem.OpenParentFolder
            });

            if (Host != null)
            {
                foreach (ManagedShell.AppBar.AppBarEdge edge in Settings.Instance.EnabledEdges)
                {
                    if (edge == Host.AppBarEdge)
                    {
                        continue;
                    }

                    builder.AddCommand(new ShellMenuCommand
                    {
                        Flags = MFT.BYCOMMAND,
                        Label = string.Format((string)FindResource("move_to_taskbar_format"), (string)FindResource(EdgeLocationResourceKey(edge))),
                        UID = (uint)MenuItem.MoveToEdgeBase + (uint)edge
                    });
                }
            }

            return builder;
        }

        private static string EdgeLocationResourceKey(ManagedShell.AppBar.AppBarEdge edge)
        {
            switch (edge)
            {
                case ManagedShell.AppBar.AppBarEdge.Left: return "location_left";
                case ManagedShell.AppBar.AppBarEdge.Top: return "location_top";
                case ManagedShell.AppBar.AppBarEdge.Right: return "location_right";
                default: return "location_bottom";
            }
        }

        private bool InvokeContextMenu(ShellFile file, bool isInteractive)
        {
            if (file == null)
            {
                return false;
            }
            
            var _ = new ShellItemContextMenu(new ShellItem[] { file }, Folder, IntPtr.Zero, HandleFileAction, isInteractive, false, new ShellMenuCommandBuilder(), GetFileCommandBuilder(file));
            return true;
        }

        private bool HandleFileAction(string action, ShellItem[] items, bool allFolders)
        {
            if (action == ((uint)MenuItem.OpenParentFolder).ToString())
            {
                ShellHelper.StartProcess(Folder.Path);
                return true;
            }

            if (uint.TryParse(action, out uint actionUid) &&
                actionUid >= (uint)MenuItem.MoveToEdgeBase && actionUid <= (uint)MenuItem.MoveToEdgeBase + 3)
            {
                ManagedShell.AppBar.AppBarEdge targetEdge = (ManagedShell.AppBar.AppBarEdge)(actionUid - (uint)MenuItem.MoveToEdgeBase);

                foreach (ShellItem item in items)
                {
                    if (item is ShellFile file)
                    {
                        Settings.Instance.SetQuickLaunchEdge(file.Path, targetEdge);
                    }
                }

                return true;
            }

            return false;
        }
        #endregion

        private void Initialize()
        {
            if (!_isLoaded && Host != null)
            {
                Settings.Instance.PropertyChanged += Settings_PropertyChanged;
                Host.hotkeyManager.TaskbarHotkeyPressed += Toolbar_TaskbarHotkeyPressed;

                _isLoaded = true;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            if (Host != null)
            {
                Host.hotkeyManager.TaskbarHotkeyPressed -= Toolbar_TaskbarHotkeyPressed;
            }

            _isLoaded = false;
        }

        private static void HostChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Toolbar toolbar && e.OldValue == null && e.NewValue != null)
            {
                toolbar.Initialize();
            }
        }
    }
}