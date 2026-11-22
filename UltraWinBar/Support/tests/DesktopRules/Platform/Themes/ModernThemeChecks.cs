using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using UltraWinBar.Utilities;

// The Modern Glass theme: the rules it is built on (plain brushes only, so it stays fast), the accent colors,
// and that its controls draw. The part that needs WPF resources runs inside ThemeChecks' application.
internal static class ModernThemeChecks
{
    // Tailwind CSS colors (MIT), typed in again here: the 400 and the 500 shade of each accent.
    private static readonly (string Name, string Light, string Strong)[] Tailwind =
    {
        ("cyan", "22D3EE", "06B6D4"), ("violet", "A78BFA", "8B5CF6"), ("fuchsia", "E879F9", "D946EF"),
        ("emerald", "34D399", "10B981"), ("amber", "FBBF24", "F59E0B"), ("rose", "FB7185", "F43F5E"),
    };

    internal static void Run(string[] args, DirectoryInfo repositoryRoot)
    {
        string project = Path.Combine(repositoryRoot.FullName, "UltraWinBar");
        string[] files =
        {
            Path.Combine(project, "Assets", "Themes", "Other", "Modern Glass.xaml"),
            Path.Combine(project, "Assets", "Resources", "Brand", "ModernControls.xaml"),
            Path.Combine(project, "Assets", "Resources", "Brand", "ModernMenus.xaml"),
        };
        foreach (string file in files)
        {
            var xml = XDocument.Load(file);
            foreach (var element in xml.Descendants())
            {
                string name = element.Name.LocalName;
                if (name.EndsWith("Effect") || name.Contains("Animation") || name.Contains("Storyboard") ||
                    name.Contains("EventTrigger") || name.Contains("GradientBrush"))
                    throw new Exception($"{Path.GetFileName(file)}: <{name}> is not allowed (no effects, animations or gradients).");
                bool layered = (string)element.Attribute("AllowsTransparency") is "True" or "true" ||
                    ((string)element.Attribute("Property") == "AllowsTransparency" && (string)element.Attribute("Value") is "True" or "true");
                if (layered) throw new Exception($"{Path.GetFileName(file)}: layered (transparent) windows are not allowed.");
                if (name == "Popup" && (string)element.Attribute("PopupAnimation") != "None")
                    throw new Exception($"{Path.GetFileName(file)}: a popup without PopupAnimation=\"None\".");
                if ((string)element.Attribute("Property") is "Effect" or "PopupAnimation" or "BitmapEffect")
                    throw new Exception($"{Path.GetFileName(file)}: a style may not set {(string)element.Attribute("Property")}.");
            }
            if (File.ReadLines(file).Count() > 1000) throw new Exception(Path.GetFileName(file) + " is over 1000 lines.");
        }
        Console.WriteLine("PASS: Modern Glass is plain brushes: no effects, animations, gradients, layered windows or animated popups.");

        if (!ModernTheme.Accents.SequenceEqual(Tailwind.Select(color => color.Name)))
            throw new Exception("The accents are not the six Tailwind colors in order.");
        foreach (var (name, light, strong) in Tailwind)
        {
            var (actualLight, actualStrong) = ModernTheme.ColorsOf(name);
            if (Hex(actualLight) != light || Hex(actualStrong) != strong)
                throw new Exception($"Accent {name} is {Hex(actualLight)}/{Hex(actualStrong)}, Tailwind has {light}/{strong}.");
            // Dark text (slate-950) is set on the accent: readable on the 400 shade, and on the 500 shade as a large UI mark.
            if (Contrast(actualLight, Color.FromRgb(0x02, 0x06, 0x17)) < 4.5 || Contrast(actualStrong, Color.FromRgb(0x02, 0x06, 0x17)) < 4.0)
                throw new Exception($"Dark text on the {name} accent is too faint.");
        }
        if (ModernTheme.IsAccent("pink") || ModernTheme.IsAccent("") || ModernTheme.IsAccent(null)) throw new Exception("An unknown accent is accepted.");
        string theme = File.ReadAllText(files[0]);
        if (!theme.Contains("x:Key=\"ModernAccent\" Color=\"#22D3EE\"")) throw new Exception("The theme's default accent is not cyan-400.");

        var settings = new Settings();
        if (settings.Theme != ModernTheme.ThemeName || settings.AccentColor != "cyan")
            throw new Exception("A new installation does not start with Modern Glass in cyan.");
        settings.AccentColor = "not-a-color";
        if (settings.AccentColor != "cyan") throw new Exception("An unknown accent in the settings file is not turned into cyan.");
        settings.AccentColor = "rose";
        if (settings.AccentColor != "rose") throw new Exception("A known accent was not kept.");
        Console.WriteLine("PASS: the six accents are the Tailwind colors, readable under dark text; Modern Glass in cyan is the default and a wrong accent falls back to cyan.");
    }

    // Inside the application of ThemeChecks, with the Modern Glass dictionary loaded.
    internal static void CheckInApp(Application app)
    {
        var settings = Settings.Instance;
        string theme = settings.Theme, accent = settings.AccentColor;
        try
        {
            settings.Theme = ModernTheme.ThemeName;
            foreach (var (name, light, strong) in Tailwind)
            {
                settings.AccentColor = name;
                ModernTheme.Apply();
                CheckBrush(app, "ModernAccent", light);
                CheckBrush(app, "ModernAccentStrong", strong);
                if (app.Resources["ModernAccentGlow1"] is not SolidColorBrush { IsFrozen: true } glow || glow.Color.A != 0x40 || Hex(glow.Color) != light)
                    throw new Exception("The glow ring of " + name + " is not the accent at 25 percent.");
                var box = new CheckBox { IsChecked = true, Content = "Box" };
                var pixel = Render(box, out double height, 8, (int)((Math.Ceiling(Layout(box).Height) - 16) / 2) + 2);
                if (Math.Abs(pixel.R - Convert.ToInt32(light.Substring(0, 2), 16)) > 3 || Math.Abs(pixel.G - Convert.ToInt32(light.Substring(2, 2), 16)) > 3 ||
                    Math.Abs(pixel.B - Convert.ToInt32(light.Substring(4, 2), 16)) > 3)
                    throw new Exception($"A checked box is {pixel.R:X2}{pixel.G:X2}{pixel.B:X2} under the {name} accent, expected {light}.");
            }
            settings.Theme = "Windows XP Royale Noir";
            ModernTheme.Apply();
            if (app.Resources.Contains("ModernAccent") || app.Resources.Contains(SystemColors.HighlightBrushKey))
                throw new Exception("The accent stays in the application's resources after another theme is chosen.");
        }
        finally
        {
            settings.Theme = theme;
            settings.AccentColor = accent;
            ModernTheme.Apply();
        }
        Console.WriteLine("PASS: each accent reaches the controls (a checked box paints it) and leaves with the theme.");
    }

    // The controls of the settings windows, the menus and the trees draw with the theme and take room.
    internal static void RenderControls(Application app)
    {
        var combo = new ComboBox { Width = 160 };
        combo.Items.Add("One");
        combo.Items.Add("Two");
        combo.SelectedIndex = 0;
        var tabs = new TabControl { Width = 220, Height = 80 };
        tabs.Items.Add(new TabItem { Header = "First", Content = new TextBlock { Text = "a" } });
        tabs.Items.Add(new TabItem { Header = "Second", Content = new TextBlock { Text = "b" } });
        var list = new ListBox { Width = 140, Height = 70 };
        list.Items.Add("One");
        list.Items.Add("Two");
        list.SelectedIndex = 0;
        var tree = new TreeView { Width = 160, Height = 80 };
        var node = new TreeViewItem { Header = "Node", IsExpanded = true };
        node.Items.Add(new TreeViewItem { Header = "Leaf" });
        tree.Items.Add(node);
        var table = new ListView { Width = 160, Height = 70 };
        var columns = new GridView();
        columns.Columns.Add(new GridViewColumn { Header = "Name", Width = 100, DisplayMemberBinding = new System.Windows.Data.Binding(".") });
        table.View = columns;
        table.Items.Add("Row");
        var controls = new (string Name, Control Control)[]
        {
            ("Button", new Button { Content = "Button", IsDefault = true }),
            ("CheckBox", new CheckBox { Content = "Check", IsChecked = null }),
            ("TextBox", new TextBox { Text = "Text", Width = 120 }),
            ("GroupBox", new GroupBox { Header = "Group", Content = new TextBlock { Text = "x" }, Width = 160 }),
            ("ComboBox", combo),
            ("TabControl", tabs),
            ("ListBox", list),
            ("TreeView", tree),
            ("ListView", table),
            ("Slider", new Slider { Minimum = 0, Maximum = 10, Value = 4, Width = 160 }),
            ("ScrollBar", new ScrollBar { Orientation = Orientation.Vertical, Height = 100, Maximum = 10, ViewportSize = 2 }),
            ("ScrollBar", new ScrollBar { Orientation = Orientation.Horizontal, Width = 100, Maximum = 10, ViewportSize = 2 }),
            ("MenuItem", new MenuItem { Header = "Item", IsCheckable = true, IsChecked = true, InputGestureText = "Ctrl+X" }),
            ("ToolTip", new ToolTip { Content = "Tip" }),
        };
        foreach (var (name, control) in controls)
        {
            var size = Layout(control);
            if (size.Width <= 0 || size.Height <= 0 || control.Template == null)
                throw new Exception($"{name} of Modern Glass took no room: {size}.");
        }
        var separator = new Separator();
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Item" });
        menu.Items.Add(separator);
        var submenu = new MenuItem { Header = "More" };
        submenu.Items.Add(new MenuItem { Header = "Inner" });
        menu.Items.Add(submenu);
        if (!menu.ApplyTemplate() && menu.Template == null) throw new Exception("The context menu has no template.");
        foreach (string key in new[] { "ModernBase", "ModernGlass", "ModernGlassBorder", "ModernAccent", "ModernAccentSoft", "ModernText", "ModernInput" })
            if (app.TryFindResource(key) is not SolidColorBrush) throw new Exception("Modern Glass brush missing or not plain: " + key);
        Console.WriteLine("PASS: Modern Glass draws buttons, boxes, tabs, lists, trees, tables, scroll bars, slider, menus and tool tips.");
    }

    private static Size Layout(FrameworkElement control)
    {
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, Math.Max(control.DesiredSize.Width, 1), Math.Max(control.DesiredSize.Height, 1)));
        control.UpdateLayout();
        return new Size(control.ActualWidth, control.ActualHeight);
    }

    private static (int R, int G, int B) Render(FrameworkElement control, out double height, int x, int y)
    {
        var size = Layout(control);
        height = size.Height;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0]);
    }

    private static void CheckBrush(Application app, string key, string hex)
    {
        if (app.Resources[key] is not SolidColorBrush { IsFrozen: true } brush || Hex(brush.Color) != hex)
            throw new Exception($"{key} is not the plain frozen brush {hex}.");
    }

    private static string Hex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static double Contrast(Color a, Color b)
    {
        double Luminance(Color color)
        {
            double Channel(byte value)
            {
                double c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
