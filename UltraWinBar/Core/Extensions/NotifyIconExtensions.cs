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
        // Legacy records (GUID or "path:UID:tooltip") are matched by exact/prefix comparison and
        // migrated to the stable key the first time they're touched.
        private static bool IsLegacyMatch(string identifier, string stableKey, bool hasGuid)
        {
            if (identifier == stableKey) return true;
            if (hasGuid) return false; // GUID identifiers never carried a tooltip suffix
            return identifier.StartsWith(stableKey + ":", StringComparison.Ordinal);
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

            // Prefer the most recently written record (legacy duplicates were appended on each
            // tooltip change; the last match is the newest one).
            var chosen = current[matches[matches.Count - 1]];

            if (matches.Count > 1 || chosen.Identifier != stableKey)
            {
                var migrated = new List<NotifyIconBehaviorSetting>(current.Count - matches.Count + 1);
                int chosenIndex = matches[matches.Count - 1];
                for (int i = 0; i < current.Count; i++)
                {
                    if (i == chosenIndex)
                    {
                        migrated.Add(new NotifyIconBehaviorSetting { Identifier = stableKey, Behavior = chosen.Behavior });
                    }
                    else if (!matches.Contains(i))
                    {
                        migrated.Add(current[i]);
                    }
                }

                // One save for the whole migration, even if several duplicates were dropped.
                Settings.Instance.NotifyIconBehaviors = migrated;
            }

            return chosen.Behavior;
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
