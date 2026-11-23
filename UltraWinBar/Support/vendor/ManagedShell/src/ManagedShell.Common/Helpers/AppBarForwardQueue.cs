using System;
using System.Collections.Concurrent;
using System.Threading;
using ManagedShell.Common.Logging;

namespace ManagedShell.Common.Helpers
{
    /// <summary>
    /// UltraWinBar: the AppBar messages of the program's own panels (the reservation of their strip of the screen) are
    /// forwarded to Explorer from here, in order, on one background thread. Explorer answers only after it has told
    /// every window about the new work area, which times out or takes seconds on a busy desktop, and the answer to
    /// a panel's own reservation is never used: waiting for it on the UI thread froze the panels at every start.
    /// </summary>
    public static class AppBarForwardQueue
    {
        private static readonly BlockingCollection<Action> Pending = new BlockingCollection<Action>();
        private static readonly object Gate = new object();
        private static Thread worker;

        /// <summary>The callback message that marks an AppBar message as one of the program's own panels' (0: none).</summary>
        public static uint OwnCallbackMessage { get; set; }

        public static bool IsOwnReservation(uint callbackMessage) => OwnCallbackMessage != 0 && callbackMessage == OwnCallbackMessage;

        /// <summary>Queues a send to Explorer; false when it could not be queued (the caller then sends it itself).</summary>
        public static bool Post(Action send)
        {
            try
            {
                lock (Gate)
                {
                    if (worker == null)
                    {
                        worker = new Thread(Run) { IsBackground = true, Name = "AppBar forward to Explorer" };
                        worker.Start();
                    }
                }
                Pending.Add(send);
                return true;
            }
            catch (Exception e) when (e is InvalidOperationException || e is OutOfMemoryException)
            {
                return false;
            }
        }

        /// <summary>Waits, for at most the timeout, until everything queued so far has been sent.</summary>
        public static bool Flush(TimeSpan timeout)
        {
            using (var sent = new ManualResetEventSlim(false))
            {
                return Post(() => sent.Set()) && sent.Wait(timeout);
            }
        }

        private static void Run()
        {
            foreach (Action send in Pending.GetConsumingEnumerable())
            {
                long started = Environment.TickCount64;
                try
                {
                    send();
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"AppBarForwardQueue: a send to Explorer failed: {e.Message}");
                }

                long took = Environment.TickCount64 - started;
                if (took >= 300)
                {
                    ShellLogger.Debug($"AppBarForwardQueue: Explorer took {took} ms to take a panel's AppBar message (the UI thread did not wait)");
                }
            }
        }
    }
}
