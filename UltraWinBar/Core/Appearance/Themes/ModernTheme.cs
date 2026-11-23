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
    /// taken away again when another theme is chosen. The accent colors the whole look: the surfaces are the slate
    /// tinted with it, the glass layers and hairlines are the accent itself at low opacity.
    /// </summary>
    internal static class ModernTheme
    {
        internal const string ThemeName = "Modern Glass";
        // The same theme made light (slate-200, not white): the theme of a new install.
        internal const string LightThemeName = "Modern Glass Light";

        // The accents: Tailwind CSS colors (MIT), the 400 shade and the 500 shade of each.
        internal static readonly string[] Accents = { "cyan", "violet", "fuchsia", "emerald", "amber", "rose" };

        // The slate under the accent: Tailwind slate-900, 950 and 800, and and the grays that the system controls without a template of their own use.
        private static readonly Color Slate900 = Color.FromRgb(0x0F, 0x17, 0x2A);
        private static readonly Color Slate950 = Color.FromRgb(0x02, 0x06, 0x17);
        private static readonly Color Slate800 = Color.FromRgb(0x1E, 0x29, 0x3B);
        private static readonly Color Slate700 = Color.FromRgb(0x33, 0x41, 0x55);
        private static readonly Color Slate600 = Color.FromRgb(0x47, 0x55, 0x69);
        // The light surfaces: Tailwind slate-100, 200, 300 and 400.
        private static readonly Color Slate100 = Color.FromRgb(0xF1, 0xF5, 0xF9);
        private static readonly Color Slate200 = Color.FromRgb(0xE2, 0xE8, 0xF0);
        private static readonly Color Slate300 = Color.FromRgb(0xCB, 0xD5, 0xE1);
        private static readonly Color Slate400 = Color.FromRgb(0x94, 0xA3, 0xB8);

        private static readonly object[] OwnKeys =
        {
            "ModernBase", "ModernBaseDeep", "ModernRaised", "ModernInput", "ModernWindowFill",
            "ModernGlass", "ModernGlassHover", "ModernGlassPressed", "ModernGlassBorder", "ModernGlassBorderStrong", "ListRowHover", "ListRowHoverEdge",
            "ModernAccent", "ModernAccentStrong", "ModernAccentGlow1", "ModernAccentGlow2", "ModernAccentGlow3", "ModernAccentSoft",
            "ButtonFace", "TaskbarBackground", "TaskButtonBackground", "ButtonFlashingFace", "InputLanguageBackground", "ToolTipBackground",
            SystemColors.WindowBrushKey, SystemColors.ControlBrushKey, SystemColors.ControlLightBrushKey, SystemColors.ControlLightLightBrushKey,
            SystemColors.ControlDarkBrushKey, SystemColors.ControlDarkDarkBrushKey,
            SystemColors.MenuBrushKey, SystemColors.MenuBarBrushKey, SystemColors.InfoBrushKey, SystemColors.ScrollBarBrushKey,
            SystemColors.HighlightBrushKey, SystemColors.HotTrackBrushKey,
        };

        private static readonly string[] PanelKeys = { "FlatActiveBorder", "FlatActiveBar", "FlatPanelEdge" };

        // Dense enough that buttons stay readable over a white page behind the window.
        internal const byte WindowGlassAlpha = 0xE0;
        internal const byte PanelGlassAlpha = 0xA6;

        private static bool applied;

        internal static bool IsAccent(string name) => Array.IndexOf(Accents, name) >= 0;

        internal static bool IsActive => Settings.Instance.Theme == ThemeName || Settings.Instance.Theme == LightThemeName;

        internal static bool IsLight => Settings.Instance.Theme == LightThemeName;

        // The accent as the theme paints it: on the light glass one shade deeper (the 500 shade, and darker for its strong one).
        private static (Color Light, Color Strong) AccentOf(string name)
        {
            var (light, strong) = ColorsOf(name);
            return IsLight ? (strong, Darker(strong, 0.18)) : (light, strong);
        }

        internal static (Color Light, Color Strong) ColorsOf(string name) => name switch
        {
            "violet" => (Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0x8B, 0x5C, 0xF6)),
            "fuchsia" => (Color.FromRgb(0xE8, 0x79, 0xF9), Color.FromRgb(0xD9, 0x46, 0xEF)),
            "emerald" => (Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x10, 0xB9, 0x81)),
            "amber" => (Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0xF5, 0x9E, 0x0B)),
            "rose" => (Color.FromRgb(0xFB, 0x71, 0x85), Color.FromRgb(0xF4, 0x3F, 0x5E)),
            _ => (Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0x06, 0xB6, 0xD4)),
        };

        // The window, the deep field of inputs, the raised layer of menus and tool tips, and the panel.
        internal static (Color Base, Color Deep, Color Raised, Color Panel) SurfacesOf(string accent)
        {
            Color light = ColorsOf(accent).Light;
            if (IsLight)
                return (Mix(Slate200, light, 0.08), Mix(Slate300, light, 0.08), Mix(Slate100, light, 0.06), Mix(Slate200, light, 0.14));
            return (Mix(Slate900, light, 0.14), Mix(Slate950, light, 0.10), Mix(Slate800, light, 0.20), Mix(Slate900, light, 0.18));
        }

        // Before FlatTheme.Apply: the panel's colors are read from the brushes put here.
        internal static void Apply()
        {
            var resources = Application.Current.Resources;
            if (!IsActive)
            {
                if (applied)
                {
                    foreach (object key in OwnKeys) resources.Remove(key);
                    foreach (string key in PanelKeys) resources.Remove(key);
                    applied = false;
                }
                return;
            }

            string accent = Settings.Instance.AccentColor;
            var (light, strong) = AccentOf(accent);
            var (back, deep, raised, panel) = SurfacesOf(accent);
            bool bright = IsLight;
            Put(resources, "ModernBase", back);
            Put(resources, "ModernBaseDeep", deep);
            Put(resources, "ModernRaised", raised);
            Put(resources, "ModernInput", WithAlpha(deep, 0x99));
            // The window is a translucent layer over the blurred desktop; opaque when there is no blur.
            Put(resources, "ModernWindowFill", WithAlpha(Darker(back, bright ? 0.03 : 0.12), ModernBackdrop.Wanted ? WindowGlassAlpha : (byte)0xFF));
            Put(resources, "ModernGlass", WithAlpha(light, 0x14));
            Put(resources, "ModernGlassHover", WithAlpha(light, 0x26));
            Put(resources, "ModernGlassPressed", WithAlpha(light, 0x38));
            Put(resources, "ModernGlassBorder", WithAlpha(light, 0x40));
            Put(resources, "ModernGlassBorderStrong", WithAlpha(light, 0x73));
            Put(resources, "ListRowHover", WithAlpha(light, 0x26));
            Put(resources, "ListRowHoverEdge", WithAlpha(light, 0x66));
            Put(resources, "ModernAccent", light);
            Put(resources, "ModernAccentStrong", strong);
            Put(resources, "ModernAccentGlow1", WithAlpha(light, 0x40));
            Put(resources, "ModernAccentGlow2", WithAlpha(light, 0x1F));
            Put(resources, "ModernAccentGlow3", WithAlpha(light, 0x0F));
            Put(resources, "ModernAccentSoft", WithAlpha(light, 0x29));
            Put(resources, "ButtonFace", panel);
            Put(resources, "TaskbarBackground", panel);
            Put(resources, "TaskButtonBackground", WithAlpha(light, 0x22));
            Put(resources, "ButtonFlashingFace", WithAlpha(light, 0x66));
            Put(resources, "InputLanguageBackground", light);
            Put(resources, "ToolTipBackground", raised);
            Put(resources, SystemColors.WindowBrushKey, back);
            Put(resources, SystemColors.ControlBrushKey, raised);
            Put(resources, SystemColors.ControlLightBrushKey, Mix(bright ? Slate300 : Slate700, light, bright ? 0.08 : 0.20));
            Put(resources, SystemColors.ControlLightLightBrushKey, Mix(bright ? Slate100 : Slate600, light, bright ? 0.06 : 0.20));
            Put(resources, SystemColors.ControlDarkBrushKey, Mix(bright ? Slate400 : Slate700, light, bright ? 0.08 : 0.20));
            Put(resources, SystemColors.ControlDarkDarkBrushKey, raised);
            Put(resources, SystemColors.MenuBrushKey, back);
            Put(resources, SystemColors.MenuBarBrushKey, back);
            Put(resources, SystemColors.InfoBrushKey, raised);
            Put(resources, SystemColors.ScrollBarBrushKey, raised);
            Put(resources, SystemColors.HighlightBrushKey, strong);
            Put(resources, SystemColors.HotTrackBrushKey, light);
            applied = true;
        }

        // After FlatTheme.Apply: the accent on the panel's own brushes.
        internal static void ApplyPanel()
        {
            if (!IsActive) return;
            var resources = Application.Current.Resources;
            Color light = AccentOf(Settings.Instance.AccentColor).Light;
            Put(resources, "FlatActiveBorder", WithAlpha(light, 0x99));
            Put(resources, "FlatActiveBar", light);
            Put(resources, "FlatPanelEdge", WithAlpha(light, 0x80));
            if (!ModernBackdrop.PanelWanted) return;

            // The panel is translucent over the blurred desktop (the panel is the layered window that the blur needs):
            // what lies on it is a layer of the accent, not a solid color.
            Put(resources, "FlatPanelFill", WithAlpha(SurfacesOf(Settings.Instance.AccentColor).Panel, PanelGlassAlpha));
            Put(resources, "FlatRest", Colors.Transparent);
            Put(resources, "FlatTask", WithAlpha(light, 0x2E));
            Put(resources, "FlatHover", WithAlpha(light, 0x47));
            Put(resources, "FlatActive", WithAlpha(light, 0x5C));
            Put(resources, "FlatPressed", WithAlpha(light, 0x73));
            Put(resources, "FlatBorder", WithAlpha(light, 0x59));
            // The tray's icons lie on a layer of the glass too, not on an opaque (and, in the light theme, near white) block.
            Put(resources, "TrayBackground", WithAlpha(light, 0x1A));
            Put(resources, "TrayVerticalBackground", WithAlpha(light, 0x1A));
        }

        private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));

        private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

        private static Color Darker(Color color, double by) =>
            Color.FromArgb(color.A, (byte)(color.R * (1 - by)), (byte)(color.G * (1 - by)), (byte)(color.B * (1 - by)));

        private static void Put(ResourceDictionary resources, object key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // A window of the theme gets a dark title bar and the blurred desktop behind it (a light title bar and none
        // of the blur under any other theme), also when the theme or the blur setting is changed while the window
        // is open. DWM draws both, so they cost the program nothing.
        internal static void Track(Window window)
        {
            bool blurred = false;
            // The close button of the theme's own title bar.
            window.CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(window)));

            void Style()
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int dark = IsActive && !IsLight ? 1 : 0;
                try
                {
                    // Attribute 20 since Windows 10 2004, 19 on the first builds that had a dark frame.
                    if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                        DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
                }
                catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
                {
                }

                bool wanted = ModernBackdrop.Wanted;
                if (wanted == blurred) return;
                if (ModernBackdrop.Set(window, wanted))
                {
                    blurred = wanted;
                }
                else if (wanted)
                {
                    // DWM refused: from now on the windows paint themselves opaque.
                    ModernBackdrop.MarkFailed();
                    Apply();
                }
            }

            EventHandler<PropertyChangedEventArgs> changed = (_, e) =>
            {
                if (e.PropertyName == nameof(Settings.Theme) || e.PropertyName == nameof(Settings.AllowBlurBehind)) Style();
            };
            window.SourceInitialized += (_, _) => Style();
            WeakSubscriptions.SubscribeSettings(changed);
            window.Closed += (_, _) => WeakSubscriptions.UnsubscribeSettings(changed);
        }
    }
}
