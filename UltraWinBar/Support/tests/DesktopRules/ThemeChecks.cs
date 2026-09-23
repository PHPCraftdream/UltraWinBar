using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class ThemeChecks
{
    internal static void Run()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { CheckThemes(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Theme verification failed", failure);
    }

    private static void CheckThemes()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string root = AppContext.BaseDirectory;
        var files = Directory.GetFiles(Path.Combine(root, "Themes"), "*.xaml");
        var preview = new DrawingVisual();
        using var drawing = preview.RenderOpen();
        int row = 0;
        foreach (string file in files)
        {
            var xml = XDocument.Load(file);
            if (xml.Descendants().Any(node => node.Name.LocalName is "LinearGradientBrush" or "RadialGradientBrush"))
                throw new Exception("Gradient remains: " + Path.GetFileName(file));
            app.Resources = new ResourceDictionary();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                Source = new Uri("pack://application:,,,/UltraWinBar;component/Assets/Languages/Group01/English.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                Source = new Uri("pack://application:,,,/UltraWinBar;component/Assets/Themes/Core/System.xaml") });
            var flatTheme = typeof(UltraWinBar.Utilities.TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.FlatTheme");
            flatTheme.GetMethod("LoadResources", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            flatTheme.GetMethod("LoadResources", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (app.Resources.MergedDictionaries.Count(dictionary => dictionary.Source?.ToString().Contains("FlatControls.xaml") == true) != 1)
                throw new Exception("Flat templates missing or duplicated after replacing application resources.");
            if (Path.GetFileName(file) != "System.xaml")
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(file) });
            app.Resources["GlobalFontFamily"] = new FontFamily("Segoe UI");
            flatTheme.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            SettingsLayoutChecks.Check();
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, row * 44, 1200, 44));
            drawing.DrawText(new FormattedText(Path.GetFileNameWithoutExtension(file), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, 1), new Point(2, row * 44 + 12));
            int column = 0;
            foreach (string key in new[] { "Taskbar", "Tray", "TaskButton", "TaskButtonActive", "TaskButtonFlashing",
                "ToolbarButton", "StartButton", "ShowDesktopButton", "TaskListScrollButton", "TrayToggleButton", "ToolbarThumb" })
            {
                if (app.TryFindResource(key) is not Style style) continue;
                var control = (Control)Activator.CreateInstance(style.TargetType);
                control.Style = style;
                if (control is ContentControl content) content.Content = new TextBlock { Text = "Test" };
                if (key == "StartButton" && control is ContentControl start)
                    start.Content = new Image { Style = (Style)app.FindResource("StartIcon") };
                control.DataContext = new { State = key == "TaskButtonActive" ? "Active" : key == "TaskButtonFlashing" ? "Flashing" : "Inactive" };
                control.Measure(new Size(160, 40));
                control.Arrange(new Rect(0, 0, 160, 40));
                control.UpdateLayout();
                var bitmap = new RenderTargetBitmap(160, 40, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(control);
                if (key is "Taskbar" or "TaskButton" or "TaskButtonActive" or "TaskButtonFlashing" or "StartButton" or "TrayToggleButton")
                    drawing.DrawImage(bitmap, new Rect(210 + column++ * 160, row * 44 + 2, 160, 40));
                if (key == "TaskButton")
                {
                    var border = control.Template.FindName("Surface", control) as Border
                        ?? throw new Exception("Flat task surface missing: " + file);
                    if (border.Background is not SolidColorBrush) throw new Exception("Task surface is not solid: " + file);
                    if (border.Margin.Right < 2 || border.Margin.Bottom < 2)
                        throw new Exception("Task buttons have no separation: " + file);
                }
                if (key == "TrayToggleButton" && control.Template.FindName("Arrow", control) == null)
                    throw new Exception("Tray toggle arrow missing: " + file);
            }
            row++;
            typeof(UltraWinBar.Utilities.DictionaryManager).GetMethod("RemoveThemeDictionaries", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { app.Resources.MergedDictionaries });
            if (app.Resources.MergedDictionaries.Count != 2)
                throw new Exception("Old theme dictionaries were retained or unrelated dictionaries were removed.");
        }
        drawing.Close();
        var sheet = new RenderTargetBitmap(1200, files.Length * 44, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(preview);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        using (var output = File.Create(Path.Combine(Path.GetTempPath(), "ultrawinbar-flat-themes.png"))) encoder.Save(output);
        app.Shutdown();
        Console.WriteLine($"PASS: {files.Length} flat themes loaded and controls rendered off-screen.");
        Console.WriteLine("PASS: settings tabs stay within 360/640-DIP bounds in every theme; theme dictionaries do not accumulate.");
    }
}
