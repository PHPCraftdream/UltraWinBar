using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class EnvironmentChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        if (Array.IndexOf(args, "--themes") >= 0) ThemeChecks.Run();
        if (ReleaseEndpoints.Releases != "https://github.com/PHPCraftdream/UltraWinBar/releases" ||
            ReleaseEndpoints.LatestReleaseApi != "https://api.github.com/repos/PHPCraftdream/UltraWinBar/releases/latest")
            throw new Exception("Release endpoints do not target this project.");
        if (Array.IndexOf(args, "--desktop-interop") >= 0)
        {
            using var desktopActions = new DesktopActions();
            var desktops = DesktopActions.GetDesktops();
            if (desktops.Count == 0) throw new Exception("No virtual desktops enumerated.");
            string sessionDesktopPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{System.Diagnostics.Process.GetCurrentProcess().SessionId}\VirtualDesktops";
            using var sessionDesktopKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sessionDesktopPath);
            using var globalDesktopKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
            byte[] currentDesktopBytes = sessionDesktopKey?.GetValue("CurrentVirtualDesktop") as byte[] ??
                globalDesktopKey?.GetValue("CurrentVirtualDesktop") as byte[];
            if (currentDesktopBytes?.Length == 16 && !desktops.Any(desktop => desktop.Id == new Guid(currentDesktopBytes)))
                throw new Exception("The active registry desktop ID is absent from the desktop list.");
            if (currentDesktopBytes?.Length == 16) Console.WriteLine("PASS: active session desktop ID matches the registered desktop list.");
            bool foundView = false;
            foreach (var hwnd in DesktopInteropProbe.VisibleWindows())
            {
                try
                {
                    string appId = desktopActions.GetApplicationId(hwnd);
                    bool pinned = desktopActions.IsApplicationIdPinned(appId);
                    foundView = true;
                    Console.WriteLine($"PASS: read-only desktop COM integration; desktops={desktops.Count}, appPinned={pinned}.");
                    break;
                }
                catch (COMException error) when (error.HResult == unchecked((int)0x8002802B)) { }
            }
            if (!foundView) Console.WriteLine($"SKIP: {desktops.Count} desktops found, no visible window exposes an application view.");
        }
        if (Array.IndexOf(args, "--desktop-context-interop") >= 0)
        {
            var knownDesktops = DesktopActions.GetDesktops().Select(desktop => desktop.Id).ToArray();
            var context = DesktopInteropProbe.ReadContext(knownDesktops, DesktopInteropProbe.VisibleWindows());
            Console.WriteLine($"PASS: VirtualDesktopContext resolved active desktop and a window desktop; onCurrent={context.IsCurrent}.");
        }
    }
}
