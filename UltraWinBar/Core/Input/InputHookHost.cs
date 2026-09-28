using ManagedShell.Common.Native;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;

namespace UltraWinBar.Utilities
{
    // R9-L (K18, review Н14): one dedicated thread owning the process's single WH_MOUSE_LL hook,
    // shared by every subscriber (DesktopActivationGuard, panel/element drag, toolbar drag). Before
    // this, each drag operation installed and removed its own hook on the UI thread, so a busy UI
    // thread could make Windows think the hook was unresponsive and silently drop it (Н14).
    //
    // Subscribers get events on the thread that subscribed (queued to its Dispatcher), so UI code
    // keeps running on the UI thread; only onHookThread subscribers that must decide
    // LowLevelMouseEventArgs.Handled synchronously run on the hook thread, using only data cached there.
    internal static class InputHookHost
    {
        private static readonly object Gate = new object();
        private static readonly List<(string Name, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> Handler, Dispatcher Target)> Subscribers =
            new List<(string, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs>, Dispatcher)>();
        private static LowLevelMouseHook mouseHook;
        private static DesktopActivationHookThread hookThread;

        // Exposed for tests: LowLevelMouseHook.InstalledCount counts every live hook process-wide, and
        // in a test process the only one that can exist is this host's.
        internal static int InstalledHookCount => LowLevelMouseHook.InstalledCount;
        internal static int SubscriberCount { get { lock (Gate) return Subscribers.Count; } }

        internal static IDisposable SubscribeMouse(string subscriberName, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> handler, bool onHookThread = false)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            // No Dispatcher on the subscribing thread (e.g. tests): deliver on the hook thread.
            Dispatcher target = onHookThread ? null : Dispatcher.FromThread(Thread.CurrentThread);
            lock (Gate)
            {
                if (mouseHook == null) Start();
                Subscribers.Add((subscriberName, handler, target));
            }
            return new Subscription(subscriberName, handler);
        }

        private static void Start()
        {
            var hook = new LowLevelMouseHook();
            hook.LowLevelMouseEvent += Dispatch;
            try
            {
                // Hosting on a dedicated thread keeps a stalled UI thread from lagging every mouse
                // event system-wide; reuses the generic dedicated-thread runner DesktopActivationGuard
                // already relied on for its own (now retired) private hook.
                hookThread = new DesktopActivationHookThread(hook.Initialize, () =>
                {
                    hook.LowLevelMouseEvent -= Dispatch;
                    hook.Dispose();
                });
                mouseHook = hook;
            }
            catch
            {
                hook.LowLevelMouseEvent -= Dispatch;
                throw;
            }
        }

        // Hook thread: never waits on another thread. LowLevelMouseEventArgs is a fresh by-value copy per event.
        internal static void Dispatch(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            (string Name, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> Handler, Dispatcher Target)[] snapshot;
            lock (Gate) snapshot = Subscribers.ToArray();
            foreach (var (name, handler, target) in snapshot)
            {
                if (target == null) Invoke(name, handler, sender, e);
                else if (!target.HasShutdownStarted) target.BeginInvoke(new Action(() => Invoke(name, handler, sender, e)));
            }
        }

        private static void Invoke(string name, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> handler, object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            // Unsubscribed while the event was queued: drop it.
            lock (Gate) { if (!Subscribers.Exists(s => s.Handler == handler)) return; }
            try { handler(sender, e); }
            catch (Exception error) { CallbackGuard.Report(name, error); }
        }

        private static void Unsubscribe(string subscriberName, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> handler)
        {
            DesktopActivationHookThread toDispose = null;
            lock (Gate)
            {
                Subscribers.RemoveAll(s => s.Name == subscriberName && s.Handler == handler);
                if (Subscribers.Count == 0 && hookThread != null)
                {
                    toDispose = hookThread;
                    hookThread = null;
                    mouseHook = null;
                }
            }
            // Outside the lock: Dispose joins the hook thread (up to its timeout), which must not run
            // while a concurrent SubscribeMouse is blocked waiting on the same lock.
            toDispose?.Dispose();
        }

        private sealed class Subscription : IDisposable
        {
            private readonly string name;
            private EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> handler;

            internal Subscription(string name, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> handler)
            {
                this.name = name;
                this.handler = handler;
            }

            public void Dispose()
            {
                var h = System.Threading.Interlocked.Exchange(ref handler, null);
                if (h != null) Unsubscribe(name, h);
            }
        }
    }
}
