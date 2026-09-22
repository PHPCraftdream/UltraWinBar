using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace UltraWinBar.Utilities
{
    internal static class FlatTheme
    {
        internal static void LoadResources()
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            if (!dictionaries.Any(dictionary => dictionary.Source?.ToString().EndsWith("Resources/FlatControls.xaml", StringComparison.OrdinalIgnoreCase) == true))
                dictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/UltraWinBar;component/Resources/FlatControls.xaml")
                });
        }

        internal static void Apply()
        {
            var resources = Application.Current.Resources;
            Color panel = ColorFor("TaskbarBackgroundTall", ColorFor("TaskbarBackground", ColorFor("ButtonFace", Colors.DimGray)));
            panel = Composite(panel, SystemColors.ControlColor);
            Color task = ColorFor("TaskButtonBackground", ColorFor("ButtonFace", panel));
            task = Composite(task, panel);
            Color text = Contrast(task);
            Put("FlatPanel", panel); Put("FlatTask", task); Put("FlatText", Contrast(panel)); Put("FlatTaskText", text);
            Put("FlatHover", Mix(task, text, 0.12));
            Put("FlatActive", Mix(task, text, 0.22));
            Put("FlatPressed", Mix(task, text, 0.30));
            Put("FlatBorder", Mix(task, text, 0.25));
            var icon = new DrawingImage(new GeometryDrawing(new SolidColorBrush(Contrast(panel)), null,
                Geometry.Parse("M0,0 H9 V9 H0 Z M12,0 H21 V9 H12 Z M0,12 H9 V21 H0 Z M12,12 H21 V21 H12 Z")));
            icon.Freeze();
            resources["FlatStartIcon"] = icon;
            resources["UseFloatingStartButton"] = false;
            Put("TrayBackground", panel); Put("TrayVerticalBackground", panel);
        }

        private static Color ColorFor(string key, Color fallback) =>
            Application.Current.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

        private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));

        private static Color Composite(Color color, Color background) => Mix(background, color, color.A / 255.0);
        private static Color Contrast(Color color) => (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 145
            ? Color.FromRgb(24, 24, 24) : Color.FromRgb(240, 240, 240);

        private static void Put(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
    }
}
