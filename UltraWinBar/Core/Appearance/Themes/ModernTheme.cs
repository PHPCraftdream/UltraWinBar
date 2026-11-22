using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The neon accent of the Modern Glass theme and the dark frame of its windows. The theme's dictionary holds the
    /// default (cyan) brushes; the accent chosen in the settings is put over them at the application level, and
    /// taken away again when another theme is chosen.
    /// </summary>
    internal static class ModernTheme
    {
        internal const string ThemeName = "Modern Glass";

        // The accents: Tailwind CSS colors (MIT), the 400 shade and the 500 shade of each.
        internal static readonly string[] Accents = { "cyan", "violet", "fuchsia", "emerald", "amber", "rose" };

        private static readonly object[] OwnKeys =
        {
            "ModernAccent", "ModernAccentStrong", "ModernAccentGlow1", "ModernAccentGlow2", "ModernAccentGlow3", "ModernAccentSoft",
            "ButtonFlashingFace", "InputLanguageBackground", SystemColors.HighlightBrushKey, SystemColors.HotTrackBrushKey
        };

        private static bool applied;

        internal static bool IsAccent(string name) => Array.IndexOf(Accents, name) >= 0;

        internal static (Color Light, Color Strong) ColorsOf(string name) => name switch
        {
            "violet" => (Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0x8B, 0x5C, 0xF6)),
            "fuchsia" => (Color.FromRgb(0xE8, 0x79, 0xF9), Color.FromRgb(0xD9, 0x46, 0xEF)),
            "emerald" => (Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x10, 0xB9, 0x81)),
            "amber" => (Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0xF5, 0x9E, 0x0B)),
            "rose" => (Color.FromRgb(0xFB, 0x71, 0x85), Color.FromRgb(0xF4, 0x3F, 0x5E)),
            _ => (Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0x06, 0xB6, 0xD4)),
        };

        // After FlatTheme.Apply: its panel brushes are the defaults that the accent replaces here.
        internal static void Apply()
        {
            var resources = Application.Current.Resources;
            if (Settings.Instance.Theme != ThemeName)
            {
                if (applied)
                {
                    foreach (object key in OwnKeys) resources.Remove(key);
                    applied = false;
                }
                return;
            }

            var (light, strong) = ColorsOf(Settings.Instance.AccentColor);
            Put(resources, "ModernAccent", light);
            Put(resources, "ModernAccentStrong", strong);
            Put(resources, "ModernAccentGlow1", WithAlpha(light, 0x40));
            Put(resources, "ModernAccentGlow2", WithAlpha(light, 0x1F));
            Put(resources, "ModernAccentGlow3", WithAlpha(light, 0x0F));
            Put(resources, "ModernAccentSoft", WithAlpha(light, 0x29));
            Put(resources, "ButtonFlashingFace", WithAlpha(light, 0x66));
            Put(resources, "InputLanguageBackground", light);
            Put(resources, SystemColors.HighlightBrushKey, strong);
            Put(resources, SystemColors.HotTrackBrushKey, light);
            Put(resources, "FlatActiveBorder", WithAlpha(light, 0x99));
            Put(resources, "FlatActiveBar", light);
            Put(resources, "FlatPanelEdge", WithAlpha(light, 0x59));
            applied = true;
        }

        private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

        private static void Put(ResourceDictionary resources, object key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // A window of the theme gets a dark title bar (and a light one under any other theme), also when the theme
        // is changed while the window is open. DWM draws the frame, so it costs the program nothing.
        internal static void Track(Window window)
        {
            EventHandler<PropertyChangedEventArgs> changed = (_, e) =>
            {
                if (e.PropertyName == nameof(Settings.Theme)) StyleFrame(window);
            };
            window.SourceInitialized += (_, _) => StyleFrame(window);
            WeakSubscriptions.SubscribeSettings(changed);
            window.Closed += (_, _) => WeakSubscriptions.UnsubscribeSettings(changed);
        }

        private static void StyleFrame(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int dark = Settings.Instance.Theme == ThemeName ? 1 : 0;
            try
            {
                // Attribute 20 since Windows 10 2004, 19 on the first builds that had a dark frame.
                if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }
    }
}
