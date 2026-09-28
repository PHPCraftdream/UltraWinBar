using ManagedShell.WindowsTray;
using UltraWinBar.Utilities;
using System;
using System.Collections.Generic;

namespace UltraWinBar.Extensions
{
    public static class NotifyIconExtensions
    {
        public static string GetInvertIdentifier(this NotifyIcon icon)
        {
            if (icon.GUID != default) return icon.GUID.ToString();
            else return icon.Path + ":" + icon.UID.ToString();
        }

        // Icon.Identifier includes the tooltip for icons without a GUID, so it changes whenever
        // the tooltip does (unread count, battery %, ...). Behavior is keyed by the stable
        // GetInvertIdentifier() form instead: GUID, or "path:UID" without the tooltip.
        // Legacy "path:UID:tooltip" records still match; SetBehavior rewrites them to the stable key.
        private static bool IsLegacyMatch(string identifier, string stableKey, bool hasGuid)
        {
            if (string.Equals(identifier, stableKey, StringComparison.OrdinalIgnoreCase)) return true;
            if (hasGuid) return false; // GUID identifiers never carried a tooltip suffix
            return identifier.StartsWith(stableKey + ":", StringComparison.OrdinalIgnoreCase);
        }

        private static List<int> FindStableMatches(List<NotifyIconBehaviorSetting> settings, string stableKey, bool hasGuid)
        {
            var matches = new List<int>();
            for (int i = 0; i < settings.Count; i++)
            {
                if (IsLegacyMatch(settings[i].Identifier, stableKey, hasGuid))
                {
                    matches.Add(i);
                }
            }
            return matches;
        }

        // Read-only: called from tray view filters, where writing Settings would re-enter them.
        public static NotifyIconBehavior GetBehavior(this NotifyIcon icon)
        {
            string stableKey = icon.GetInvertIdentifier();
            bool hasGuid = icon.GUID != default;
            var current = Settings.Instance.NotifyIconBehaviors;
            var matches = FindStableMatches(current, stableKey, hasGuid);
            if (matches.Count == 0)
            {
                return NotifyIconBehavior.HideWhenInactive;
            }

            // A stable record wins; otherwise the last legacy one (appended on each tooltip change) is the newest.
            foreach (int index in matches)
            {
                if (string.Equals(current[index].Identifier, stableKey, StringComparison.OrdinalIgnoreCase)) return current[index].Behavior;
            }
            return current[matches[matches.Count - 1]].Behavior;
        }

        public static void SetBehavior(this NotifyIcon icon, NotifyIconBehavior behavior)
        {
            string stableKey = icon.GetInvertIdentifier();
            bool hasGuid = icon.GUID != default;
            var settings = new List<NotifyIconBehaviorSetting>(Settings.Instance.NotifyIconBehaviors);
            var matches = FindStableMatches(settings, stableKey, hasGuid);

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                settings.RemoveAt(matches[i]);
            }

            if (behavior != NotifyIconBehavior.HideWhenInactive)
            {
                settings.Add(new NotifyIconBehaviorSetting
                {
                    Identifier = stableKey,
                    Behavior = behavior
                });
            }

            Settings.Instance.NotifyIconBehaviors = settings;

            if (icon.IsPinned != (behavior == NotifyIconBehavior.AlwaysShow))
            {
                // Update pinned values in ManagedShell
                if (behavior == NotifyIconBehavior.AlwaysShow)
                {
                    icon.Pin();
                }
                else
                {
                    icon.Unpin();
                }
            }
            else
            {
                // Trigger a refresh of the collections
                icon.OnPropertyChanged("IsPinned");
            }
        }

        public static bool CanInvert(this NotifyIcon icon) {
            return Settings.Instance.InvertNotifyIcons.Contains(icon.GetInvertIdentifier());
        }

        public static void SetCanInvert(this NotifyIcon icon, bool canInvert)
        {
            var identifier = icon.GetInvertIdentifier();
            var settings = new List<string>(Settings.Instance.InvertNotifyIcons);
            var changed = false;

            if (!canInvert)
            {
                changed = settings.Remove(identifier);
            }
            else if (!settings.Contains(identifier))
            {
                settings.Add(identifier);
                changed = true;
            }

            if (changed)
            {
                Settings.Instance.InvertNotifyIcons = settings;
            }
        }
    }
}
