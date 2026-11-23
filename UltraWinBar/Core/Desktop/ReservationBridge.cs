using ManagedShell.AppBar;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace UltraWinBar.Utilities
{
    // After a restart on purpose, hidden windows hold the panels' strips as shell AppBars from the first moment of the
    // new process until its panels have published their own. Without them Explorer recomputed the work area several
    // times while it dropped the dead panels of the last run, and maximized windows jumped back and forth.
    internal static class ReservationBridge
    {
        private static readonly object Gate = new object();
        private static SynchronizationContext context;
        private static List<NativeWindow> windows;
        private static bool released;
        internal const string HeldEventName = @"Local\UltraWinBarStripsHeld";

        internal static bool IsActive { get { lock (Gate) return windows != null && !released; } }

        /// The strips between the screen's edges and the work area, one per edge that has one.
        internal static (AppBarEdge Edge, NativeMethods.Rect Rect)[] StripsFor(NativeMethods.Rect area, NativeMethods.Rect screen) => new[]
        {
            (AppBarEdge.Left, new NativeMethods.Rect(screen.Left, screen.Top, area.Left, screen.Bottom)),
            (AppBarEdge.Top, new NativeMethods.Rect(screen.Left, screen.Top, screen.Right, area.Top)),
            (AppBarEdge.Right, new NativeMethods.Rect(area.Right, screen.Top, screen.Right, screen.Bottom)),
            (AppBarEdge.Bottom, new NativeMethods.Rect(screen.Left, area.Bottom, screen.Right, screen.Bottom)),
        }.Where(strip => strip.Item2.Right > strip.Item2.Left && strip.Item2.Bottom > strip.Item2.Top).ToArray();

        internal static void Start(NativeMethods.Rect area, NativeMethods.Rect screen)
        {
            var strips = StripsFor(area, screen);
            if (strips.Length == 0) return;
            var thread = new Thread(() =>
            {
                var own = new List<NativeWindow>();
                try
                {
                    foreach (var (edge, rect) in strips)
                    {
                        var window = new NativeWindow();
                        // WS_POPUP, hidden; WS_EX_TOOLWINDOW keeps it out of the task list.
                        window.CreateHandle(new CreateParams { Style = unchecked((int)0x80000000), ExStyle = 0x80 });
                        own.Add(window);
                        PanelReservation.Reserve(window.Handle, edge, rect, false);
                    }
                }
                catch (Exception error) { ShellLogger.Debug($"ReservationBridge: {error.Message}"); }

                lock (Gate)
                {
                    windows = own;
                    if (!released)
                    {
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                        context = SynchronizationContext.Current;
                    }
                }
                ShellLogger.Debug($"ReservationBridge: holding {own.Count} strips of the last run");
                // The deploy script's holder (Hold-WorkAreaStrips.ps1) can let its strips go now.
                if (EventWaitHandle.TryOpenExisting(HeldEventName, out var held)) using (held) held.Set();
                if (context != null) Application.Run();
                Remove(own);
            }) { IsBackground = true, Name = "UltraWinBar reservation bridge" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        /// Once the panels have published their own reservations: the strips go, after them in the forward queue.
        internal static void Release()
        {
            lock (Gate)
            {
                if (released) return;
                released = true;
                context?.Post(_ => Application.ExitThread(), null);
            }
        }

        private static void Remove(List<NativeWindow> own)
        {
            foreach (var window in own)
            {
                try
                {
                    PanelReservation.Release(window.Handle);
                    window.DestroyHandle();
                }
                catch (Exception error) { ShellLogger.Debug($"ReservationBridge: {error.Message}"); }
            }
            ShellLogger.Debug("ReservationBridge: released");
        }
    }
}
