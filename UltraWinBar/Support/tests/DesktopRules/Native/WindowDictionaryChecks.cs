using System;
using ManagedShell.WindowsTasks;

// R9-N: TasksService keeps a HWND -> ApplicationWindow dictionary in sync with Windows
// (ObservableCollection) via CollectionChanged, so shell-message/WinEvent handlers can do an O(1)
// lookup instead of Windows.Any/First. This exercises the sync directly, without TasksService's
// real Initialize (which registers shell hooks against the live Explorer process).
internal static class WindowDictionaryChecks
{
    internal static void Run()
    {
        var service = new TasksService();
        IntPtr handleA = (IntPtr)0x10001;
        IntPtr handleB = (IntPtr)0x10002;
        var winA = new ApplicationWindow(service, handleA);
        var winB = new ApplicationWindow(service, handleB);

        service.Windows.Add(winA);
        service.Windows.Add(winB);
        if (service.WindowsByHandle.Count != 2 ||
            !ReferenceEquals(service.WindowsByHandle[handleA], winA) ||
            !ReferenceEquals(service.WindowsByHandle[handleB], winB))
            throw new Exception("Handle dictionary did not track both windows added to Windows.");

        service.Windows.Remove(winA);
        if (service.WindowsByHandle.ContainsKey(handleA) || service.WindowsByHandle.Count != 1)
            throw new Exception("Handle dictionary kept a stale entry after Windows.Remove.");
        if (!ReferenceEquals(service.WindowsByHandle[handleB], winB))
            throw new Exception("Handle dictionary lost an unrelated entry after removing another one.");

        service.Windows.Add(winA);
        if (service.WindowsByHandle.Count != 2)
            throw new Exception("Handle dictionary did not re-add a window after Windows.Add.");

        service.Windows.Clear();
        if (service.WindowsByHandle.Count != 0)
            throw new Exception("Handle dictionary was not cleared by Windows.Clear (a Reset notification).");

        Console.WriteLine("PASS: TasksService's HWND->ApplicationWindow dictionary stays in sync with Windows through Add, Remove, and Clear.");
        RunReplacementContinuity();
    }

    private static void RunReplacementContinuity()
    {
        using var form = new System.Windows.Forms.Form { Text = "Task replacement regression" };
        using var neighbour = new System.Windows.Forms.Form { Text = "Unrelated task regression" };
        form.Show();
        neighbour.Show();
        var service = new TasksService();
        var task = new ApplicationWindow(service, form.Handle)
        {
            State = ApplicationWindow.WindowState.Flashing,
            ProgressState = ManagedShell.Interop.NativeMethods.TBPFLAG.TBPF_NORMAL,
            ProgressValue = 63
        };
        var other = new ApplicationWindow(service, neighbour.Handle);
        service.Windows.Add(task);
        service.Windows.Add(other);
        int mutations = 0;
        service.Windows.CollectionChanged += (_, __) => mutations++;
        IntPtr handle = form.Handle;

        service.ReconcileWindowReplacement(handle);
        service.ReconcileWindowReplacement(handle);
        if (mutations != 0 || !ReferenceEquals(service.Windows[0], task) ||
            !ReferenceEquals(service.WindowsByHandle[handle], task) || !task.ShowInTaskbar ||
            task.State != ApplicationWindow.WindowState.Flashing || task.ProgressValue != 63 ||
            task.ProgressState != ManagedShell.Interop.NativeMethods.TBPFLAG.TBPF_NORMAL)
            throw new Exception("Live window replacement recreated, reordered or reset the task.");

        form.Hide();
        service.ReconcileWindowReplacement(handle);
        if (task.ShowInTaskbar || mutations != 0 || !ReferenceEquals(service.Windows[0], task))
            throw new Exception("Hidden replacement lost task identity or kept a visible button.");
        form.Show();
        service.ReconcileWindowReplacement(handle);
        if (!task.ShowInTaskbar || mutations != 0 || !ReferenceEquals(service.Windows[0], task))
            throw new Exception("Returning replacement recreated the task instead of restoring visibility.");

        form.Close();
        service.ReconcileWindowReplacement(handle);
        if (mutations != 1 || service.WindowsByHandle.ContainsKey(handle) ||
            service.Windows.Count != 1 || !ReferenceEquals(service.Windows[0], other))
            throw new Exception("Destroyed replacement left a stale task or removed an unrelated task.");
        Console.WriteLine("PASS: replacement preserves task identity, order, attention and progress; visibility follows the real window; actual destruction removes only that task.");
    }
}
