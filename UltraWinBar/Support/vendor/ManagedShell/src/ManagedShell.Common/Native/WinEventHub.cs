using System;
using System.Collections.Generic;

namespace ManagedShell.Common.Native
{
    // UltraWinBar (K12, review section 5): several UI-thread owners each registered their own
    // EVENT_SYSTEM_FOREGROUND/EVENT_OBJECT_SHOW WinEventHook with identical flags, so one OS event
    // reached the UI thread once per registration. One underlying WinEventHook per (event, flags),
    // shared by managed subscribers; the hook is installed for the first subscriber and removed
    // after the last. Only for single-event, UI-thread registrations - hooks on a dedicated thread,
    // or covering an event range, stay direct WinEventHook owners.
    internal static class WinEventHub
    {
        internal readonly struct Key : IEquatable<Key>
        {
            private readonly uint eventId;
            private readonly uint flags;
            internal Key(uint eventId, uint flags) { this.eventId = eventId; this.flags = flags; }
            public bool Equals(Key other) => eventId == other.eventId && flags == other.flags;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => unchecked((int)(eventId * 397 ^ flags));
            public override string ToString() => $"0x{eventId:X4}/0x{flags:X2}";
        }

        private sealed class Bucket
        {
            internal WinEventHook Hook;
            internal readonly List<(string Name, WinEventHook.Handler Handler)> Subscribers = new List<(string, WinEventHook.Handler)>();
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<Key, Bucket> Buckets = new Dictionary<Key, Bucket>();

        // Subscribe to one WinEvent id on the UI thread. Must be called on the thread that should own
        // the underlying hook (the first subscriber for a given (eventId, flags) pair installs it).
        // Dispose the returned subscription to stop receiving events; the underlying hook is removed
        // once the last subscriber for that (eventId, flags) pair leaves.
        internal static Subscription Subscribe(string subscriberName, uint eventId, WinEventHook.Handler handler, uint flags = WinEventHook.OutOfContext)
        {
            var key = new Key(eventId, flags);
            bool installed;
            lock (Gate)
            {
                if (!Buckets.TryGetValue(key, out Bucket bucket))
                {
                    bucket = new Bucket();
                    Buckets.Add(key, bucket);
                }
                bucket.Subscribers.Add((subscriberName, handler));
                if (bucket.Hook == null)
                {
                    bucket.Hook = new WinEventHook($"WinEventHub {key}", eventId, eventId,
                        (type, hwnd, idObject, idChild) => Dispatch(key, type, hwnd, idObject, idChild), flags);
                }
                installed = bucket.Hook.IsInstalled;
            }
            return new Subscription(key, subscriberName, handler, installed);
        }

        // Runs on the hook's owning (UI) thread, inside WinEventHook's own barrier. Isolate each
        // subscriber so one throwing handler does not skip the rest sharing this event.
        private static void Dispatch(Key key, uint eventType, IntPtr hwnd, int idObject, int idChild)
        {
            (string Name, WinEventHook.Handler Handler)[] snapshot;
            lock (Gate)
            {
                if (!Buckets.TryGetValue(key, out Bucket bucket)) return;
                snapshot = bucket.Subscribers.ToArray();
            }
            foreach (var (name, handler) in snapshot)
            {
                try { handler(eventType, hwnd, idObject, idChild); }
                catch (Exception error) { CallbackGuard.Report(name, error); }
            }
        }

        private static void Unsubscribe(Key key, string subscriberName, WinEventHook.Handler handler)
        {
            WinEventHook toDispose = null;
            lock (Gate)
            {
                if (!Buckets.TryGetValue(key, out Bucket bucket)) return;
                bucket.Subscribers.RemoveAll(s => s.Name == subscriberName && s.Handler == handler);
                if (bucket.Subscribers.Count == 0)
                {
                    toDispose = bucket.Hook;
                    Buckets.Remove(key);
                }
            }
            // Outside the lock: WinEventHook.Dispose may synchronously unhook (or marshal to the
            // owning thread), neither of which should run while holding the hub's own lock.
            toDispose?.Dispose();
        }

        internal sealed class Subscription : IDisposable
        {
            private readonly Key key;
            private readonly string name;
            private readonly WinEventHook.Handler handler;
            private bool disposed;

            // Whether the shared hook for this (event, flags) pair was installed when this
            // subscription was created (false only if SetWinEventHook itself failed).
            internal bool IsInstalled { get; }

            internal Subscription(Key key, string name, WinEventHook.Handler handler, bool isInstalled)
            {
                this.key = key;
                this.name = name;
                this.handler = handler;
                IsInstalled = isInstalled;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Unsubscribe(key, name, handler);
            }
        }
    }
}
