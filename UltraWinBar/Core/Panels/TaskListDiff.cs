using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UltraWinBar.Utilities
{
    // Reconciles an ObservableCollection to a target order in place, issuing the exact same
    // Remove/Insert/Move operation sequence a naive `Contains`/`IndexOf` diff loop would (so
    // TaskButton containers are reused identically), but with O(1) amortized membership and
    // position lookups instead of O(n) scans per item — avoiding the O(n^2) blowup from a large
    // task list.
    public static class TaskListDiff
    {
        public static void Reconcile(ObservableCollection<object> current, IReadOnlyList<object> target)
        {
            var targetSet = new HashSet<object>(target);
            for (int i = current.Count - 1; i >= 0; i--)
                if (!targetSet.Contains(current[i])) current.RemoveAt(i);

            // Mirrors current's contents: kept in sync with every Insert/Move below so the
            // "current index of an item" lookup never re-scans the collection.
            var position = new Dictionary<object, int>(current.Count);
            for (int i = 0; i < current.Count; i++) position[current[i]] = i;

            for (int i = 0; i < target.Count; i++)
            {
                object item = target[i];
                if (!position.TryGetValue(item, out int oldIndex))
                {
                    current.Insert(i, item);
                    for (int k = i; k < current.Count; k++) position[current[k]] = k;
                }
                else if (oldIndex != i)
                {
                    current.Move(oldIndex, i);
                    int lo = Math.Min(oldIndex, i), hi = Math.Max(oldIndex, i);
                    for (int k = lo; k <= hi; k++) position[current[k]] = k;
                }
            }
        }
    }
}
