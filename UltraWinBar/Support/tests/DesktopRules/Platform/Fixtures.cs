using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal sealed class PersistenceFixture : IMigratableSettings
{
    public bool MigrationPerformed => false;
    public string Value { get; set; }
}

// Stand-in for a UI dispatcher: records what was posted without running it until Pump is called.
internal sealed class RecordingSyncContext : System.Threading.SynchronizationContext
{
    public int PostCount;
    private System.Threading.SendOrPostCallback _callback;
    private object _state;

    public override void Post(System.Threading.SendOrPostCallback d, object state)
    {
        PostCount++;
        _callback = d;
        _state = state;
    }

    public void Pump()
    {
        var callback = _callback;
        var state = _state;
        _callback = null;
        callback?.Invoke(state);
    }
}

internal static class DesktopInteropProbe
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    internal static List<IntPtr> VisibleWindows()
    {
        var result = new List<IntPtr>();
        EnumWindowsProc callback = (hwnd, _) =>
        {
            if (IsWindowVisible(hwnd)) result.Add(hwnd);
            return true;
        };
        if (!EnumWindows(callback, IntPtr.Zero)) throw new InvalidOperationException("EnumWindows failed.");
        return result;
    }

    internal static (Guid CurrentDesktop, bool IsCurrent) ReadContext(Guid[] knownDesktops, List<IntPtr> windows)
    {
        Exception failure = null;
        (Guid CurrentDesktop, bool IsCurrent) result = default;
        var thread = new System.Threading.Thread(() =>
        {
            System.Windows.Application app = null;
            object context = null;
            try
            {
                app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                Type contextType = typeof(DesktopActions).Assembly.GetType("UltraWinBar.Utilities.VirtualDesktopContext");
                context = Activator.CreateInstance(contextType, true);
                var managerField = contextType.GetField("manager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                object managerProxy = managerField?.GetValue(context);
                object manager = managerProxy?.GetType().GetMethod("Get", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(managerProxy, null);
                if (manager == null)
                    throw new InvalidOperationException("IVirtualDesktopManager could not be created.");
                var managerInterface = managerField.FieldType.GetGenericArguments()[0];
                var getDesktopId = managerInterface.GetMethod("GetWindowDesktopId");
                var isWindowOnCurrent = managerInterface.GetMethod("IsWindowOnCurrentVirtualDesktop");

                var currentId = (Guid)contextType.GetProperty("CurrentId").GetValue(context);
                if (currentId == Guid.Empty || !knownDesktops.Contains(currentId))
                    throw new InvalidOperationException("The current registry desktop ID is not in the desktop list.");

                // The weak desktop subscription used by task lists/buttons must actually deliver Changed.
                var weak = typeof(DesktopActions).Assembly.GetType("UltraWinBar.Utilities.WeakSubscriptions");
                int desktopChanges = 0;
                EventHandler<EventArgs> onDesktopChanged = (_, _) => desktopChanges++;
                weak.GetMethod("SubscribeDesktopChanged", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { onDesktopChanged });
                var changedField = contextType.GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                ((EventHandler)changedField.GetValue(context))?.Invoke(context, EventArgs.Empty);
                weak.GetMethod("UnsubscribeDesktopChanged", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { onDesktopChanged });
                ((EventHandler)changedField.GetValue(context))?.Invoke(context, EventArgs.Empty);
                if (desktopChanges != 1) throw new InvalidOperationException($"Weak desktop subscription delivered {desktopChanges} of 1 changes.");

                foreach (var hwnd in windows)
                {
                    try
                    {
                        object[] desktopArguments = { hwnd, Guid.Empty };
                        if ((int)getDesktopId.Invoke(manager, desktopArguments) < 0) continue;
                        var desktopId = (Guid)desktopArguments[1];
                        if (desktopId == Guid.Empty || !knownDesktops.Contains(desktopId)) continue;
                        object[] currentArguments = { hwnd, false };
                        if ((int)isWindowOnCurrent.Invoke(manager, currentArguments) < 0) continue;
                        bool isCurrent = (bool)currentArguments[1];
                        if ((Guid)contextType.GetMethod("DesktopForWindow").Invoke(context, new object[] { hwnd }) != desktopId ||
                            (bool)contextType.GetMethod("IsOnCurrentDesktop").Invoke(context, new object[] { hwnd }) != isCurrent)
                            throw new InvalidOperationException("VirtualDesktopContext disagrees with IVirtualDesktopManager.");
                        result = (currentId, isCurrent);
                        return;
                    }
                    catch (System.Reflection.TargetInvocationException error) when (error.InnerException is COMException) { }
                }

                throw new InvalidOperationException("No visible window had a registered virtual desktop ID.");
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                try { (context as IDisposable)?.Dispose(); }
                catch (Exception error) { failure ??= error; }
                try { app?.Shutdown(); }
                catch (Exception error) { failure ??= error; }
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("VirtualDesktopContext integration failed.", failure);
        return result;
    }
}
