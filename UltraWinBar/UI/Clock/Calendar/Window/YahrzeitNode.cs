using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using UltraWinBar.Utilities;

namespace UltraWinBar
{
    /// <summary>
    /// A line of the tree of yahrzeits in the calendar settings: everyone, a category, or a figure, each with a check box.
    /// A group is checked when all its figures are, unchecked when none is, and in between otherwise; checking or
    /// unchecking a group does the same to all its figures. Only the figures are saved (those unchecked, by key).
    /// </summary>
    public sealed class YahrzeitNode : INotifyPropertyChanged
    {
        private bool? isChecked = true;
        private bool isExpanded;

        public event PropertyChangedEventHandler PropertyChanged;

        public YahrzeitNode(string text, string key = null, YahrzeitNode parent = null)
        {
            Text = text;
            Key = key;
            Parent = parent;
            Children = new List<YahrzeitNode>();
        }

        public string Text { get; }

        /// <summary>The key of the figure; null for a group.</summary>
        public string Key { get; }

        public YahrzeitNode Parent { get; }

        public List<YahrzeitNode> Children { get; }

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded == value) return;
                isExpanded = value;
                Raise();
            }
        }

        /// <summary>Raised on the root when a figure is checked or unchecked, however it came about.</summary>
        public event EventHandler FigureChanged;

        /// <summary>What the user set, or what the figures below say: true, false, or null for some of them.</summary>
        public bool? IsChecked
        {
            get => isChecked;
            set
            {
                // A click on a group takes it to all or none, never to "some".
                bool on = value ?? false;
                if (Children.Count == 0)
                {
                    if (isChecked == on) return;
                    isChecked = on;
                    Raise();
                    Parent?.Recompute();
                }
                else
                {
                    foreach (var child in Children) child.Force(on);
                    Recompute();
                    Raise();
                }
                Root.FigureChanged?.Invoke(Root, EventArgs.Empty);
            }
        }

        private YahrzeitNode Root => Parent == null ? this : Parent.Root;

        // Sets this node and everything below it, without announcing the change to the root yet.
        private void Force(bool on)
        {
            foreach (var child in Children) child.Force(on);
            if (isChecked == on) return;
            isChecked = on;
            Raise(nameof(IsChecked));
        }

        // A group from what its children say.
        private void Recompute()
        {
            if (Children.Count == 0) return;
            bool? now = Children.All(child => child.isChecked == true) ? true : Children.All(child => child.isChecked == false) ? false : (bool?)null;
            if (isChecked != now)
            {
                isChecked = now;
                Raise(nameof(IsChecked));
            }
            Parent?.Recompute();
        }

        /// <summary>The keys of the figures below (or of this one) that are not checked.</summary>
        public IEnumerable<string> UncheckedKeys()
        {
            if (Children.Count == 0) return isChecked == false && Key != null ? new[] { Key } : Array.Empty<string>();
            return Children.SelectMany(child => child.UncheckedKeys());
        }

        /// <summary>The whole tree of the figures: everyone, the categories, the figures, checked but for the keys given.</summary>
        public static YahrzeitNode Build(string language, string everyoneText, ICollection<string> hidden)
        {
            var root = new YahrzeitNode(everyoneText) { IsExpanded = true };
            foreach (string category in Yahrzeits.Categories)
            {
                var group = new YahrzeitNode(Yahrzeits.CategoryName(category, language), null, root);
                foreach (Tzaddik person in Yahrzeits.OfCategory(category))
                {
                    string when = person.Day.ToString("00") + " " + HebrewClockFormatter.MonthName(person.Month, false, language);
                    group.Children.Add(new YahrzeitNode(person.Name(language) + " — " + when, person.Key, group)
                    {
                        isChecked = hidden == null || !hidden.Contains(person.Key),
                    });
                }
                root.Children.Add(group);
            }
            foreach (var group in root.Children) group.Recompute();
            return root;
        }

        private void Raise([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
