using ManagedShell.Common.Logging;
using System;
using System.Windows.Forms;

namespace ManagedShell.Common.SupportingClasses
{
    public class NativeWindowEx : NativeWindow
    {
        public delegate void MessageReceivedEventHandler(ref Message m, ref bool handled);

        public event MessageReceivedEventHandler MessageReceived;

        protected override void WndProc(ref Message m)
        {
            bool handled = false;
            MessageReceived?.Invoke(ref m, ref handled);

            if (!handled)
            {
                base.WndProc(ref m);
            }
        }

        public override void CreateHandle(CreateParams cp)
        {
            base.CreateHandle(cp);
        }

        // NativeWindow.OnThreadException is empty by default: a WndProc exception here would
        // otherwise vanish silently instead of reaching the log. ManagedShell cannot see the
        // app's CallbackGuard, so this only logs.
        protected override void OnThreadException(Exception e) => ShellLogger.Error($"NativeWindowEx: unhandled WndProc exception: {e}");
    }
}