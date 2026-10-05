using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    // Per-desktop payload cache for instant virtual-desktop switches: each desktop keeps its
    // own live list/ItemsControl instead of one shared list being cleared and refilled.
    internal sealed class DesktopTaskLists<T> where T : class
    {
        private readonly Dictionary<Guid, T> lists = new Dictionary<Guid, T>();
        private readonly List<Guid> order = new List<Guid>(); // insertion order, for eviction

        public Guid CurrentId { get; private set; } = Guid.Empty;
        public int Count => lists.Count;
        public int MaxCached { get; set; } = 16;

        public T Current => GetOrCreate(CurrentId, null);

        public T GetOrCreate(Guid id, Func<T> factory)
        {
            if (lists.TryGetValue(id, out T existing)) return existing;
            T created = factory != null ? factory() : null;
            if (created == null) return null;
            lists[id] = created;
            order.Add(id);
            EvictOldestIfNeeded(id);
            return created;
        }

        // Switches the current desktop, creating its payload lazily; returns it.
        public T SwitchTo(Guid id, Func<T> factory)
        {
            CurrentId = id;
            return GetOrCreate(id, factory);
        }

        // Runs remove on every cached payload, including non-current ones. Returning true
        // from remove also drops that desktop's cached payload entirely.
        public void RemoveFromAll(Func<T, bool> remove)
        {
            foreach (Guid id in order.ToArray())
            {
                if (remove(lists[id]))
                {
                    lists.Remove(id);
                    order.Remove(id);
                }
            }
        }

        // Drops cached desktops that no longer exist; the current one is never dropped.
        public void PruneTo(IEnumerable<Guid> existingIds)
        {
            var keep = new HashSet<Guid>(existingIds) { CurrentId };
            foreach (Guid id in order.ToArray())
            {
                if (!keep.Contains(id))
                {
                    lists.Remove(id);
                    order.Remove(id);
                }
            }
        }

        public IEnumerable<Guid> Ids => order.ToArray();
        public IEnumerable<T> Payloads => Ids.Select(id => lists[id]);

        private void EvictOldestIfNeeded(Guid current)
        {
            while (lists.Count > MaxCached)
            {
                int oldest = order.FindIndex(id => id != current && lists.ContainsKey(id));
                if (oldest < 0) break;
                lists.Remove(order[oldest]);
                order.RemoveAt(oldest);
            }
        }
    }
}
