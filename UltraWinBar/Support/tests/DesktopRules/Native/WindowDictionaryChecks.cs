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
    }
}
