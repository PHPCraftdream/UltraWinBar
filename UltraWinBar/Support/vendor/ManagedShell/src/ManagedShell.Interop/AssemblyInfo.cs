using System.Runtime.CompilerServices;

// R9-M / К20: friend access for the sibling assemblies that call internal trust-boundary helpers
// (MemorySafety, CrossProcessMessages), and for the test harness that exercises them directly.
[assembly: InternalsVisibleTo("ManagedShell.AppBar")]
[assembly: InternalsVisibleTo("ManagedShell.WindowsTasks")]
[assembly: InternalsVisibleTo("ManagedShell.WindowsTray")]
[assembly: InternalsVisibleTo("DesktopRules")]
