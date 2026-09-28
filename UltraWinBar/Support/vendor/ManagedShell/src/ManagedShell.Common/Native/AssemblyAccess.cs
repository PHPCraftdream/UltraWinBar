using System.Runtime.CompilerServices;

// UltraWinBar (K12): CallbackGuard/WinEventHook/WinEventHub/ShellComProxy/NativeCallback stay
// internal (smaller public surface than making them part of ManagedShell's public API); grant
// friend access to the ManagedShell projects and the app that call into them directly.
[assembly: InternalsVisibleTo("ManagedShell.WindowsTray")]
[assembly: InternalsVisibleTo("ManagedShell.WindowsTasks")]
[assembly: InternalsVisibleTo("ManagedShell.AppBar")]
[assembly: InternalsVisibleTo("ManagedShell.UWPInterop")]
[assembly: InternalsVisibleTo("ManagedShell.ShellFolders")]
[assembly: InternalsVisibleTo("UltraWinBar")]
// R9-I (K14/K20): typed access for the service-restart test instead of reflection.
[assembly: InternalsVisibleTo("DesktopRules")]
