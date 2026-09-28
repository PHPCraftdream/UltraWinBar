using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ManagedShell.AppBar;
using ManagedShell.Common.Native;
using ManagedShell.WindowsTasks;
using ManagedShell.WindowsTray;
using UltraWinBar.Utilities;

// R9-I (K14, review section 6): explicit lifecycle (Created/Running/Stopped/Disposed) and a
// Stop->Start restart test for the four services named in the review that own native resources:
// TasksService, TrayService, ExplorerHelper, VirtualDesktopContext. StartMenuMonitor is excluded -
// it is being changed by other in-flight work.
//
// Safety: TasksService.Initialize registers a real shell-hook window, calls SetTaskmanWindow, and
// writes the TaskbandHWND prop onto the real Explorer tray; TrayService.Initialize registers a real
// "Shell_TrayWnd"/"TrayNotifyWnd" window class (visible to system-wide FindWindow, so it could
// collide with a real taskbar or a concurrently running UltraWinBar) and broadcasts TaskbarCreated.
// Neither is called here. Both restart contracts are instead proven two ways: (1) live, by
// calling Dispose() ten times on a never-Initialize()'d instance - this exercises the idempotent
// Stop-from-Created path with zero native calls (IsInitialized/HwndTray both stay at their default
// "never started" value throughout, so Dispose()'s early-exit branch is all that runs); (2) by
// source shape, checking Initialize() tracks a per-resource install flag for every handle/hook it
// can install and sets LifecycleState on both the success and the rollback path.
// ExplorerHelper's only native resource (a WinEventHub subscription) installs solely when
// HideExplorerTaskbar is toggled true, which would hide the real Windows taskbar - never toggled
// here, so its full Dispose() path (and a real Start-after-Dispose attempt) runs live safely.
// VirtualDesktopContext has no such landmine: its "start" is a handful of read-only COM queries
// against Explorer's already-running virtual desktop manager, the same kind of call
// --desktop-context-interop already makes elsewhere in this suite. It still needs its own opt-in
// flag rather than reusing --desktop-context-interop: WPF allows only one System.Windows.Application
// per AppDomain, ever (not just one at a time) - EnvironmentChecks' --desktop-context-interop path
// (Fixtures.cs' DesktopInteropProbe) already creates and shuts one down, and a second attempt later
// in the same process throws InvalidOperationException even though the first was disposed. Skipped
// by default, like the rest of desktop-interop; not combinable with --desktop-context-interop.
internal static class ServiceLifecycleChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        CheckTasksServiceRestartWithoutLiveExplorer(repositoryRoot);
        CheckTrayServiceRestartWithoutLiveExplorer(repositoryRoot);
        CheckExplorerHelperRestartIsIdempotent();

        if (Array.IndexOf(args, "--desktop-lifecycle-interop") >= 0)
        {
            CheckVirtualDesktopContextRestart();
        }
        else
        {
            Console.WriteLine("SKIP: VirtualDesktopContext restart cycle needs --desktop-lifecycle-interop (real Explorer COM activation).");
        }
    }

    private const uint GrGdiObjects = 0;
    private const uint GrUserObjects = 1;
    // GDI/USER objects and process handles: an exact match across ten iterations is not
    // guaranteed even with zero new native calls in the code under test, because the .NET GC can
    // finalize unrelated SafeHandles/managed wrappers (JIT, WPF's per-thread visual cache, this
    // very test process's own churn) between the two samples. A small tolerance distinguishes that
    // noise from a real per-restart leak, which would show up as steady growth, not a one-off wobble.
    private const int Tolerance = 8;

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static (int gdi, int user, int handles) SampleProcessResources()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return ((int)GetGuiResources(process.Handle, GrGdiObjects), (int)GetGuiResources(process.Handle, GrUserObjects), process.HandleCount);
    }

    private static void AssertResourcesStable(string label,
        (int gdi, int user, int handles) before, (int gdi, int user, int handles) after)
    {
        if (Math.Abs(after.gdi - before.gdi) > Tolerance)
            throw new Exception($"{label}: GDI objects moved by more than the tolerance ({before.gdi} -> {after.gdi}, tolerance {Tolerance}).");
        if (Math.Abs(after.user - before.user) > Tolerance)
            throw new Exception($"{label}: USER objects moved by more than the tolerance ({before.user} -> {after.user}, tolerance {Tolerance}).");
        if (Math.Abs(after.handles - before.handles) > Tolerance)
            throw new Exception($"{label}: process handle count moved by more than the tolerance ({before.handles} -> {after.handles}, tolerance {Tolerance}).");
        Console.WriteLine($"  {label}: gdi {before.gdi}->{after.gdi}, user {before.user}->{after.user}, handles {before.handles}->{after.handles} (tolerance {Tolerance}).");
    }

    private static string ReadVendorSource(System.IO.DirectoryInfo repositoryRoot, params string[] segments)
    {
        var pathParts = new System.Collections.Generic.List<string> { repositoryRoot.FullName, "UltraWinBar" };
        pathParts.AddRange(segments);
        return System.IO.File.ReadAllText(System.IO.Path.Combine(pathParts.ToArray()));
    }

    private static string ExtractMethodBody(string source, string methodStart, string nextMemberStart, string fileLabel)
    {
        int start = source.IndexOf(methodStart);
        int end = start < 0 ? -1 : source.IndexOf(nextMemberStart, start);
        if (start < 0 || end < 0)
            throw new Exception($"{fileLabel}: could not locate '{methodStart}' followed by '{nextMemberStart}'.");
        return source.Substring(start, end - start);
    }

    // Live: Dispose() ten times on an instance that never called Initialize(). IsInitialized never
    // becomes true, so every call takes the cheap early-exit branch - no shell hook window, no
    // WinEvent hooks, no TaskbandHWND write. Source shape: Initialize()'s rollback tracks one bool
    // per resource it can install, and both its success and failure paths set LifecycleState.
    private static void CheckTasksServiceRestartWithoutLiveExplorer(System.IO.DirectoryInfo repositoryRoot)
    {
        int hooksBefore = WinEventHook.InstalledCount;
        var before = SampleProcessResources();

        var service = new TasksService();
        if (service.LifecycleState != ServiceLifecycleState.Created)
            throw new Exception($"TasksService must start Created, was {service.LifecycleState}.");

        for (int i = 0; i < 10; i++)
        {
            service.Dispose();
            if (service.LifecycleState != ServiceLifecycleState.Stopped)
                throw new Exception($"TasksService.Dispose() (never started) must reach Stopped, was {service.LifecycleState} on iteration {i}.");
            if (service.IsInitialized)
                throw new Exception("TasksService.IsInitialized must stay false across repeated Dispose().");
        }

        int hooksAfter = WinEventHook.InstalledCount;
        var after = SampleProcessResources();
        if (hooksAfter != hooksBefore)
            throw new Exception($"TasksService.Dispose() on a never-started instance must not touch WinEventHook.InstalledCount ({hooksBefore} -> {hooksAfter}).");
        AssertResourcesStable("TasksService", before, after);

        string body = ExtractMethodBody(
            ReadVendorSource(repositoryRoot, "Support", "vendor", "ManagedShell", "src", "ManagedShell.WindowsTasks", "TasksService.cs"),
            "internal void Initialize(bool withMultiMonTracking)", "internal void SetTaskCategoryProvider", "TasksService.cs");
        foreach (var marker in new[]
        {
            "hookWinCreated", "shellHookRegistered", "messageReceivedHooked", "cloakHookInstalledHere", "moveHookInstalledHere",
            "LifecycleState = ServiceLifecycleState.Running", "LifecycleState = ServiceLifecycleState.Stopped"
        })
        {
            if (!body.Contains(marker))
                throw new Exception($"TasksService.Initialize is missing rollback/state marker '{marker}'.");
        }

        Console.WriteLine("PASS: TasksService.Dispose() is idempotent from Created (10x live, zero native calls); " +
            "Initialize()'s per-resource rollback and LifecycleState transitions verified by source shape " +
            "(a live Initialize() would register a real shell hook and write the Explorer tray's TaskbandHWND prop).");
    }

    // Same shape as TasksService above: live idempotent-Stop-from-Created, plus source shape for
    // the rollback Initialize() now performs when RegisterTrayWnd/RegisterNotifyWnd only partially
    // succeed, and for the unconditional UnregisterClass in DestroyWindows() that closes the leak.
    private static void CheckTrayServiceRestartWithoutLiveExplorer(System.IO.DirectoryInfo repositoryRoot)
    {
        int hooksBefore = WinEventHook.InstalledCount;
        var before = SampleProcessResources();

        var service = new TrayService();
        if (service.LifecycleState != ServiceLifecycleState.Created)
            throw new Exception($"TrayService must start Created, was {service.LifecycleState}.");

        for (int i = 0; i < 10; i++)
        {
            service.Dispose();
            if (service.LifecycleState != ServiceLifecycleState.Stopped)
                throw new Exception($"TrayService.Dispose() (never started) must reach Stopped, was {service.LifecycleState} on iteration {i}.");
        }

        int hooksAfter = WinEventHook.InstalledCount;
        var after = SampleProcessResources();
        if (hooksAfter != hooksBefore)
            throw new Exception($"TrayService.Dispose() on a never-started instance must not touch WinEventHook.InstalledCount ({hooksBefore} -> {hooksAfter}).");
        AssertResourcesStable("TrayService", before, after);

        string body = ExtractMethodBody(
            ReadVendorSource(repositoryRoot, "Support", "vendor", "ManagedShell", "src", "ManagedShell.WindowsTray", "TrayService.cs"),
            "internal IntPtr Initialize()", "internal void Run()", "TrayService.cs");
        foreach (var marker in new[]
        {
            "LifecycleState == ServiceLifecycleState.Disposed", "LifecycleState = ServiceLifecycleState.Running", "LifecycleState = ServiceLifecycleState.Stopped"
        })
        {
            if (!body.Contains(marker))
                throw new Exception($"TrayService.Initialize is missing rollback/state marker '{marker}'.");
        }

        string destroyWindowsBody = ExtractMethodBody(
            ReadVendorSource(repositoryRoot, "Support", "vendor", "ManagedShell", "src", "ManagedShell.WindowsTray", "TrayService.cs"),
            "private void DestroyWindows()", "public void Dispose()", "TrayService.cs");
        if (System.Text.RegularExpressions.Regex.Matches(destroyWindowsBody, @"UnregisterClass\(").Count != 2)
            throw new Exception("TrayService.DestroyWindows() must call UnregisterClass unconditionally for both window classes, not only when a window of that class exists.");

        Console.WriteLine("PASS: TrayService.Dispose() is idempotent from Created (10x live, zero native calls); " +
            "Initialize()'s rollback, LifecycleState transitions and DestroyWindows()'s unconditional UnregisterClass " +
            "verified by source shape (a live Initialize() would register a real Shell_TrayWnd class and broadcast TaskbarCreated).");
    }

    // Live: ExplorerHelper's only native resource installs on demand (HideExplorerTaskbar=true),
    // never triggered here, so this exercises the real Dispose() path with no side effect on the
    // real Windows taskbar - including a genuine Start-after-Dispose attempt (which must be ignored).
    private static void CheckExplorerHelperRestartIsIdempotent()
    {
        var before = SampleProcessResources();

        for (int i = 0; i < 10; i++)
        {
            var helper = new ExplorerHelper();
            if (helper.LifecycleState != ServiceLifecycleState.Running)
                throw new Exception($"ExplorerHelper must be Running immediately after construction (it has no separate Start method), was {helper.LifecycleState} on iteration {i}.");

            helper.Dispose();
            helper.Dispose(); // second Dispose() must be a no-op
            if (helper.LifecycleState != ServiceLifecycleState.Disposed)
                throw new Exception($"ExplorerHelper.Dispose() must reach Disposed, was {helper.LifecycleState} on iteration {i}.");

            helper.HideExplorerTaskbar = true; // Start after Dispose: must be ignored (logged), not reinstall the hook
            if (helper.HideExplorerTaskbar)
                throw new Exception("ExplorerHelper.HideExplorerTaskbar must not take effect after Dispose().");
        }

        var after = SampleProcessResources();
        AssertResourcesStable("ExplorerHelper", before, after);
        Console.WriteLine("PASS: ExplorerHelper is Running immediately after construction, Dispose() is idempotent (10x live), " +
            "and a Start-after-Dispose attempt (HideExplorerTaskbar=true) is ignored rather than reinstalling the hook.");
    }

    // Live, opt-in: VirtualDesktopContext's constructor and Dispose() make real (read-only) COM
    // calls against Explorer's virtual desktop manager and real registry-watch handles. Runs on its
    // own STA thread with a throwaway Application, mirroring DesktopInteropProbe.ReadContext.
    private static void CheckVirtualDesktopContextRestart()
    {
        Exception failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            System.Windows.Application app = null;
            try
            {
                app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

                var before = SampleProcessResources();

                for (int i = 0; i < 10; i++)
                {
                    var context = new VirtualDesktopContext();
                    if (context.LifecycleState != ServiceLifecycleState.Running)
                        throw new Exception($"VirtualDesktopContext must be Running immediately after construction, was {context.LifecycleState} on iteration {i}.");

                    context.Dispose();
                    context.Dispose(); // second Dispose() must be a no-op
                    if (context.LifecycleState != ServiceLifecycleState.Disposed)
                        throw new Exception($"VirtualDesktopContext.Dispose() must reach Disposed, was {context.LifecycleState} on iteration {i}.");
                }

                var after = SampleProcessResources();
                AssertResourcesStable("VirtualDesktopContext", before, after);
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                try { app?.Shutdown(); }
                catch (Exception error) { failure ??= error; }
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("VirtualDesktopContext restart cycle failed.", failure);

        Console.WriteLine("PASS: VirtualDesktopContext Created->Running->Disposed 10x for real " +
            "(real Explorer COM activation via --desktop-lifecycle-interop); GDI/USER/handle counts stable.");
    }
}
