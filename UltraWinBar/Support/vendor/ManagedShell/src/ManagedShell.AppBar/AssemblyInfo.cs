using System.Runtime.CompilerServices;

// R9-M / К20: lets the test harness call internal members by type instead of by reflected name.
[assembly: InternalsVisibleTo("DesktopRules")]
