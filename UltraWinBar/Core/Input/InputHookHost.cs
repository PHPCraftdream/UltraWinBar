using ManagedShell.Common.Native;
using System;
using System.Collections.Generic;

namespace UltraWinBar.Utilities
{
    // R9-L (K18, review Н14): one dedicated thread owning the process's single WH_MOUSE_LL hook,
    // shared by every subscriber (DesktopActivationGuard, panel/element drag, toolbar drag). Before
    // this, each drag operation installed and removed its own hook on the UI thread, so a busy UI
    // thread could make Windows think the hook was unresponsive and silently drop it (Н14).
    //
    // The underlying LowLevelMouseHook still calls each subscriber directly on the hook thread (same
    // contract LowLevelMouseHook always had) so a subscriber that must make a synchronous swallow
    // decision (setting LowLevelMouseEventArgs.Handled) still can, using only data already cached on
    // the hook thread - never by calling into the UI thread. Subscribers that don't need that must
    // marshal everything else via their own Dispatcher.BeginInvoke, exactly as they already did; this
    // host only guarantees a single shared installation, per-subscriber exception isolation, and
    // install-on-first/remove-after-last lifecycle.
    internal static class InputHookHost
    {
        private static readonly object Gate = new object();
        private static readonly List<(string Name, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> Handler)> Subscribers =
            new List<(string, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs>)>();
        private static LowLevelMouseHook mouseHook;
        private static DesktopActivationHookThread hookThread;

        // Exposed for tests: LowLevelMouseHook.InstalledCount counts every live hook process-wide, and
        // in a test process the only one that can exist is this host's.
        internal static int InstalledHookCount => LowLevelMouseHook.InstalledCount;
        internal static int SubscriberCount { get { lock (Gate) return Subscribers.Count; } }

        internal static IDisposable SubscribeMouse(string subscriberName, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (Gate)
            {
                if (mouseHook == null) Start();
                Subscribers.Add((subscriberName, handler));
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

        // Hook thread: copies (LowLevelMouseEventArgs/MSLLHOOKSTRUCT are already a fresh, by-value
        // copy from LowLevelMouseHook.MouseHookProc) then calls each subscriber directly - isolated
        // by try/catch so one throwing subscriber does not skip the rest sharing this hook. Never
        // touches the UI thread here; subscribers are responsible for their own Dispatcher.BeginInvoke.
        private static void Dispatch(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            (string Name, EventHandler<LowLevelMouseHook.LowLevelMouseEventArgs> Handler)[] snapshot;
            lock (Gate) snapshot = Subscribers.ToArray();
            foreach (var (name, handler) in snapshot)
            {
                try { handler(sender, e); }
                catch (Exception error) { CallbackGuard.Report(name, error); }
            }
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
