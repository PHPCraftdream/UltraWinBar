using ManagedShell.Common.Logging;
using System;
using System.Runtime.InteropServices;

namespace ManagedShell.Common.Native
{
    // UltraWinBar (K12): moved from the app so ManagedShell's own Explorer-hosted COM objects can
    // use the same recreate-with-backoff policy. One policy for COM objects hosted by explorer.exe
    // (they die with it): detect a severed proxy, drop it, recreate lazily with 2/5/15/60 s backoff,
    // and start over on every Explorer restart.
    internal static class ShellCom
    {
        private const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
        private const int RPC_S_SERVER_UNAVAILABLE = unchecked((int)0x800706BA);
        private const int CO_E_OBJNOTCONNECTED = unchecked((int)0x800401FD);
        internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);

        internal static bool IsDisconnected(int hr) =>
            hr == RPC_E_DISCONNECTED || hr == RPC_S_SERVER_UNAVAILABLE || hr == CO_E_OBJNOTCONNECTED;

        internal static bool IsDisconnected(Exception error) =>
            error is InvalidComObjectException || (error is COMException && IsDisconnected(error.HResult));

        internal static TimeSpan NextRetryDelay(TimeSpan current)
        {
            if (current < TimeSpan.FromSeconds(5)) return TimeSpan.FromSeconds(5);
            if (current < TimeSpan.FromSeconds(15)) return TimeSpan.FromSeconds(15);
            return TimeSpan.FromSeconds(60);
        }
    }

    // Thread-safe holder of one Explorer-hosted object. Consumers call Get() on every use and never
    // cache the result; Check()/Report() feed call outcomes back so a dead proxy is replaced.
    internal sealed class ShellComProxy<T> : IDisposable where T : class
    {
        private readonly string name;
        private readonly Func<T> create;
        private readonly Action<T> release;
        private readonly object gate = new object();
        private T value;
        private DateTime nextRetryUtc = DateTime.MinValue;
        private TimeSpan retryDelay = ShellCom.InitialRetryDelay;
        private bool failureLogged;
        private string lastError;
        private bool disposed;
        // Set while create() runs outside the lock (it may pump messages on an STA thread and
        // re-enter Get() on the same thread, or race a concurrent caller on another thread).
        private bool creating;

        // Raised (outside the lock) when an object is created after a period without one.
        internal event Action Recovered;
        // Raised when a live object is dropped (severed proxy or Explorer restart).
        internal event Action Dropped;

        internal ShellComProxy(string name, Func<T> create, Action<T> release)
        {
            this.name = name;
            this.create = create;
            this.release = release;
            value = TryCreate();
            ExplorerLifecycle.Restarted += ExplorerLifecycle_Restarted;
        }

        internal bool HasValue { get { lock (gate) return value != null; } }

        // Current object, lazily recreated with backoff; null while unavailable.
        internal T Get()
        {
            lock (gate)
            {
                if (disposed) return null;
                if (value != null) return value;
                // Someone else's create() is in flight (possibly this same thread, re-entered while
                // create() pumps messages for STA activation). Do not start a second activation.
                if (creating) return null;
                DateTime now = DateTime.UtcNow;
                if (now < nextRetryUtc) return null;
                creating = true;
            }

            // Runs outside the lock: cross-process activation can pump messages and re-enter Get().
            T created = TryCreate();
            T extra = null;
            T result = null;
            lock (gate)
            {
                creating = false;
                if (disposed || value != null)
                {
                    // Disposed while creating, or the slot got filled meanwhile: drop the extra object.
                    extra = created;
                }
                else if (created == null)
                {
                    if (!failureLogged)
                    {
                        ShellLogger.Warning($"{name}: unavailable ({lastError}); will keep retrying.");
                        failureLogged = true;
                    }
                    nextRetryUtc = DateTime.UtcNow + retryDelay;
                    retryDelay = ShellCom.NextRetryDelay(retryDelay);
                }
                else
                {
                    value = created;
                    result = created;
                    if (failureLogged)
                    {
                        ShellLogger.Info($"{name}: recreated.");
                        failureLogged = false;
                    }
                }
            }
            if (extra != null) Release(extra);
            if (result != null) Recovered?.Invoke();
            return result;
        }

        internal int Check(int hr)
        {
            if (ShellCom.IsDisconnected(hr)) Drop($"HRESULT 0x{hr:X8}", backOff: true);
            return hr;
        }

        internal void Report(Exception error)
        {
            if (ShellCom.IsDisconnected(error)) Drop(error.Message, backOff: true);
        }

        // Explorer registers its classes at runtime, possibly after TaskbarCreated: retry from scratch.
        private void ExplorerLifecycle_Restarted(object sender, EventArgs e)
        {
            Drop("explorer restarted", backOff: false);
            lock (gate)
            {
                nextRetryUtc = DateTime.MinValue;
                retryDelay = ShellCom.InitialRetryDelay;
            }
            Get();
        }

        private void Drop(string reason, bool backOff)
        {
            T stale;
            lock (gate)
            {
                stale = value;
                if (stale == null) return;
                value = null;
                if (backOff)
                {
                    nextRetryUtc = DateTime.UtcNow + retryDelay;
                    retryDelay = ShellCom.NextRetryDelay(retryDelay);
                }
            }
            Release(stale);
            if (backOff) ShellLogger.Warning($"{name}: disconnected ({reason}); will recreate on next use.");
            Dropped?.Invoke();
        }

        private T TryCreate()
        {
            try { return create(); }
            catch (Exception error)
            {
                lastError = error.Message;
                return null;
            }
        }

        private void Release(T stale)
        {
            try { release?.Invoke(stale); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            ExplorerLifecycle.Restarted -= ExplorerLifecycle_Restarted;
            T stale;
            lock (gate)
            {
                disposed = true;
                stale = value;
                value = null;
            }
            if (stale != null) Release(stale);
        }
    }
}
