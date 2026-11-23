using System;
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// A search line at the top of a long drop-down list (time zones, countries): what is typed keeps the items whose text
    /// contains it, in any case. The chosen item always stays in the list, so that filtering never changes the choice; the
    /// filter goes when the list closes. Enter takes the first item left, Escape closes. The line is part of the Modern Glass
    /// drop-down (PART_Search); under the other themes typing the start of an item still finds it.
    /// </summary>
    public static class ComboSearch
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(ComboSearch), new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            if (element is not ComboBox combo) return;
            combo.DropDownOpened -= Opened;
            combo.DropDownClosed -= Closed;
            if ((bool)e.NewValue)
            {
                combo.DropDownOpened += Opened;
                combo.DropDownClosed += Closed;
            }
        }

        private static TextBox SearchOf(ComboBox combo) => combo.Template?.FindName("PART_Search", combo) as TextBox;

        private static void Opened(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            TextBox search = SearchOf(combo);
            if (search == null) return;
            search.Text = "";
            search.TextChanged -= SearchChanged;
            search.TextChanged += SearchChanged;
            search.PreviewKeyDown -= SearchKeyDown;
            search.PreviewKeyDown += SearchKeyDown;
            search.Tag = combo;
            // After the drop-down has put the focus on its items.
            combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { search.Focus(); Keyboard.Focus(search); }));
        }

        private static void Closed(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            if (SearchOf(combo) is TextBox search) search.Text = "";
            if (combo.ItemsSource != null) CollectionViewSource.GetDefaultView(combo.ItemsSource).Filter = null;
        }

        private static void SearchChanged(object sender, TextChangedEventArgs e)
        {
            var search = (TextBox)sender;
            if (search.Tag is not ComboBox combo || combo.ItemsSource == null) return;
            string query = search.Text.Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(combo.ItemsSource);
            object chosen = combo.SelectedItem;
            view.Filter = query.Length == 0 ? null : item => Equals(item, chosen) || TextOf(combo, item).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static void SearchKeyDown(object sender, KeyEventArgs e)
        {
            var search = (TextBox)sender;
            if (search.Tag is not ComboBox combo) return;
            if (e.Key == Key.Escape)
            {
                combo.IsDropDownOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // The first item the search left, other than the one already chosen (which always stays in the list).
                object first = null;
                if (search.Text.Trim().Length > 0 && combo.ItemsSource != null)
                {
                    IEnumerator left = CollectionViewSource.GetDefaultView(combo.ItemsSource).GetEnumerator();
                    while (left.MoveNext())
                    {
                        if (Equals(left.Current, combo.SelectedItem)) continue;
                        first = left.Current;
                        break;
                    }
                }
                if (first != null) combo.SelectedItem = first;
                combo.IsDropDownOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Down && combo.ItemContainerGenerator.ContainerFromIndex(0) is ComboBoxItem item)
            {
                item.Focus();
                e.Handled = true;
            }
        }

        // The text the list shows for an item: its DisplayMemberPath property, or the item itself.
        private static string TextOf(ComboBox combo, object item)
        {
            if (item == null) return "";
            string path = combo.DisplayMemberPath;
            if (!string.IsNullOrEmpty(path))
            {
                PropertyInfo property = item.GetType().GetProperty(path);
                if (property != null) return property.GetValue(item)?.ToString() ?? "";
            }
            return item.ToString() ?? "";
        }
    }
}
