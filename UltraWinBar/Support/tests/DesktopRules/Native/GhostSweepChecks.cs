using System;
using System.Windows.Forms;
using ManagedShell.WindowsTasks;

// Н12: TasksService.SweepGhosts removes handle-dictionary entries whose HWND no longer exists (a
// shell-hook WINDOWDESTROYED message can be lost under a UI-thread stall / posted-message queue
// overflow) and counts them. Uses real NativeWindow handles to get a genuinely dead HWND, instead
// of TasksService.Initialize, which would hook the live Explorer process.
internal static class GhostSweepChecks
{
    internal static void Run()
    {
        var service = new TasksService();

        var deadNativeWindow = new NativeWindow();
        deadNativeWindow.CreateHandle(new CreateParams());
        IntPtr deadHandle = deadNativeWindow.Handle;

        var liveNativeWindow = new NativeWindow();
        liveNativeWindow.CreateHandle(new CreateParams());
        IntPtr liveHandle = liveNativeWindow.Handle;

        var deadWindow = new ApplicationWindow(service, deadHandle);
        var liveWindow = new ApplicationWindow(service, liveHandle);

        service.Windows.Add(deadWindow);
        service.Windows.Add(liveWindow);
        if (!service.WindowsByHandle.ContainsKey(deadHandle) || !service.WindowsByHandle.ContainsKey(liveHandle))
            throw new Exception("Setup failed: both windows must be tracked before destroying one.");

        deadNativeWindow.DestroyHandle(); // deadHandle is now stale; IsWindow(deadHandle) == false

        int before = TasksService.GhostsRemoved;
        try
        {
            service.SweepGhosts();
        }
        finally
        {
            liveNativeWindow.DestroyHandle();
        }
        int after = TasksService.GhostsRemoved;

        if (service.WindowsByHandle.ContainsKey(deadHandle))
            throw new Exception("SweepGhosts left a destroyed window's handle in the dictionary.");
        if (service.Windows.Contains(deadWindow))
            throw new Exception("SweepGhosts did not remove the destroyed window from Windows.");
        if (!service.WindowsByHandle.ContainsKey(liveHandle) || !service.Windows.Contains(liveWindow))
            throw new Exception("SweepGhosts removed a window whose HWND is still alive.");
        if (after != before + 1)
            throw new Exception($"SweepGhosts must count exactly one removed ghost (before={before}, after={after}).");

        Console.WriteLine("PASS: SweepGhosts removes a window whose HWND has been destroyed, leaves live windows alone, and counts the removal.");
    }
}
