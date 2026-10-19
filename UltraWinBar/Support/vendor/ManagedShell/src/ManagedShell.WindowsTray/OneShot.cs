using System;
using System.Threading;

namespace ManagedShell.WindowsTray
{
    // UltraWinBar: runs its action once, from whichever trigger comes first, and never after Cancel.
    internal sealed class OneShot
    {
        private readonly Action _action;
        private int _spent;

        public OneShot(Action action)
        {
            _action = action;
        }

        public bool Fire()
        {
            if (Interlocked.Exchange(ref _spent, 1) != 0)
            {
                return false;
            }

            _action();
            return true;
        }

        public void Cancel()
        {
            Interlocked.Exchange(ref _spent, 1);
        }
    }
}
