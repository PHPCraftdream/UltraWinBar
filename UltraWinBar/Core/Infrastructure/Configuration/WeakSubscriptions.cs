using System;
using System.ComponentModel;
using System.Windows;

namespace UltraWinBar.Utilities
{
    // UI objects subscribe to app-lifetime sources (Settings, virtual desktops) weakly: a panel,
    // list or button that misses an unsubscribe can no longer be kept alive by the singleton.
    internal static class WeakSubscriptions
    {
        internal static void SubscribeSettings(EventHandler<PropertyChangedEventArgs> handler) =>
            PropertyChangedEventManager.AddHandler(Settings.Instance, handler, string.Empty);

        internal static void UnsubscribeSettings(EventHandler<PropertyChangedEventArgs> handler) =>
            PropertyChangedEventManager.RemoveHandler(Settings.Instance, handler, string.Empty);

        internal static void SubscribeDesktopChanged(EventHandler<EventArgs> handler)
        {
            if (VirtualDesktopContext.Instance is VirtualDesktopContext desktops)
                WeakEventManager<VirtualDesktopContext, EventArgs>.AddHandler(desktops, nameof(VirtualDesktopContext.Changed), handler);
        }

        internal static void UnsubscribeDesktopChanged(EventHandler<EventArgs> handler)
        {
            if (VirtualDesktopContext.Instance is VirtualDesktopContext desktops)
                WeakEventManager<VirtualDesktopContext, EventArgs>.RemoveHandler(desktops, nameof(VirtualDesktopContext.Changed), handler);
        }
    }
}
