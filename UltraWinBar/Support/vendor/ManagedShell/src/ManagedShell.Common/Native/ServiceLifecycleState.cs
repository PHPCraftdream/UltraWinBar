namespace ManagedShell.Common.Native
{
    // R9-I (K14, review section 6): explicit lifecycle for services that own native resources
    // (hooks, windows, handles), replacing an implicit bool "is initialized" flag. Start/Stop are
    // idempotent for every user of this enum; see each service for what Start-after-Stopped does.
    internal enum ServiceLifecycleState
    {
        Created,
        Running,
        Stopped,
        Disposed
    }
}
