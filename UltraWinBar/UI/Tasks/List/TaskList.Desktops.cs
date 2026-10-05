using ManagedShell.WindowsTasks;
using UltraWinBar.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace UltraWinBar.Controls
{
    // One live ItemsControl + ObservableCollection per virtual desktop, so switching desktops
    // only flips Visibility instead of clearing/refilling a shared list (which recreated every
    // TaskButton and cost 100-250ms).
    public partial class TaskList
    {
        private readonly DesktopTaskLists<ItemsControl> desktopLists = new();
        private readonly Dictionary<Guid, ObservableCollection<object>> desktopDisplayed = new();
        private readonly Dictionary<Guid, IReadOnlyDictionary<object, string>> desktopKeys = new();
        private static readonly IReadOnlyDictionary<object, string> EmptyKeys = new Dictionary<object, string>();

        // The current desktop's ItemsControl / items / display keys; existing call sites unchanged.
        private ItemsControl TasksList => desktopLists.Current ??
            desktopLists.SwitchTo(VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty, CreateDesktopList);
        private IReadOnlyDictionary<object, string> displayKeys =>
            desktopKeys.TryGetValue(desktopLists.CurrentId, out var keys) ? keys : EmptyKeys;

        private ObservableCollection<object> displayedTasks
        {
            get
            {
                Guid id = desktopLists.CurrentId;
                if (!desktopDisplayed.TryGetValue(id, out ObservableCollection<object> list))
                    desktopDisplayed[id] = list = new ObservableCollection<object>();
                return list;
            }
        }

        private ItemsControl CreateDesktopList()
        {
            var control = (ItemsControl)((DataTemplate)Resources["TaskListItemsTemplate"]).LoadContent();
            control.ItemsSource = new ObservableCollection<object>();
            TasksListsHost.Children.Add(control);
            return control;
        }

        // Flip to the current desktop's cached list (lazily created) before the model pass runs.
        private void SwitchDesktop()
        {
            Guid id = VirtualDesktopContext.Instance?.CurrentId ?? Guid.Empty;
            ItemsControl list = desktopLists.SwitchTo(id, CreateDesktopList);
            desktopDisplayed[id] = (ObservableCollection<object>)list.ItemsSource;

            foreach (UIElement child in TasksListsHost.Children)
                child.Visibility = child == list ? Visibility.Visible : Visibility.Collapsed;

            // Non-current lists keep stale widths; keep the live one in sync immediately.
            list.AlternationCount = desktopDisplayed[id].Count;
            desktopLists.PruneTo(VirtualDesktopContext.ReadExistingDesktopIds().Concat(new[] { id }).Distinct());
            DropDroppedDesktops();
            SetTaskButtonWidth();
        }

        // Drops per-desktop side state whose ItemsControl pruning already removed.
        private void DropDroppedDesktops()
        {
            foreach (Guid id in desktopDisplayed.Keys.ToArray())
            {
                if (id == desktopLists.CurrentId) continue;
                if (!desktopLists.Ids.Contains(id))
                {
                    desktopDisplayed.Remove(id);
                    desktopKeys.Remove(id);
                }
            }

            foreach (ItemsControl orphan in TasksListsHost.Children.OfType<ItemsControl>().ToArray())
                if (orphan != TasksList && !desktopLists.Payloads.Contains(orphan))
                    TasksListsHost.Children.Remove(orphan);
        }

        // A window left the source (closed/untracked): purge it from every desktop's list,
        // not just the visible one, so switching back never resurrects a dead button.
        internal void RemoveWindowEverywhere(ApplicationWindow window)
        {
            VirtualDesktopContext.Instance?.ForgetWindowDesktop(window.Handle);
            desktopLists.RemoveFromAll(list =>
            {
                if (list.ItemsSource is ObservableCollection<object> items)
                {
                    for (int i = items.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(items[i], window)) items.RemoveAt(i);
                }
                return false; // keep the desktop cached
            });

            foreach (Guid id in desktopKeys.Keys.ToArray())
                desktopKeys[id] = desktopKeys[id].Where(kv => !ReferenceEquals(kv.Key, window))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // Reset carries no per-item info, so just clear non-current collections; the next pass
        // refills the current one via Reconcile. Rare (shell restart), recreation cost accepted.
        private void ClearNonCurrentLists()
        {
            foreach (var pair in desktopDisplayed)
                if (pair.Key != desktopLists.CurrentId) pair.Value.Clear();
        }

        private void ResetDesktopLists()
        {
            desktopLists.RemoveFromAll(list =>
            {
                TasksListsHost.Children.Remove(list);
                return true; // drop the payload too
            });
            desktopDisplayed.Clear();
            desktopKeys.Clear();
        }
    }
}
