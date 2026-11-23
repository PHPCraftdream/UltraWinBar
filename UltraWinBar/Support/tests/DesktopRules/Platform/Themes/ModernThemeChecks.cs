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

    // The task label takes the panel's weight from the theme (inheritance from the button does not reach it).
    private static bool TaskLabelHasPanelWeight(string system)
    {
        int label = system.IndexOf("x:Key=\"TaskLabel\"", StringComparison.Ordinal);
        int weight = label < 0 ? -1 : system.IndexOf("<Setter Property=\"FontWeight\" Value=\"{DynamicResource PanelFontWeight}\" />", label, StringComparison.Ordinal);
        return weight > 0 && weight - label < 200;
    }

    internal static void Run(string[] args, DirectoryInfo repositoryRoot)
    {
        string project = Path.Combine(repositoryRoot.FullName, "UltraWinBar");
        // The brand dictionaries are compiled to BAML (default Page items): as Resource they were parsed as text
        // at every start (over a second). A compiled page must not name its own assembly in a clr-namespace.
        string csproj = File.ReadAllText(Path.Combine(project, "UltraWinBar.csproj"));
        foreach (string brand in new[] { "FlatControls", "ModernControls", "ModernMenus" })
        {
            if (csproj.Contains($@"<Resource Include=""Assets\Resources\Brand\{brand}.xaml"""))
                throw new Exception($"{brand}.xaml must be a compiled page, not a raw Resource.");
            if (File.ReadAllText(Path.Combine(project, "Assets", "Resources", "Brand", brand + ".xaml")).Contains(";assembly=UltraWinBar\""))
                throw new Exception($"{brand}.xaml: a compiled page cannot name its own assembly in a clr-namespace.");
        }
        // Dialogs draw their own title bar under the theme (the system's GDI caption text shows on a black box over the
        // glass); the calendar, which has its own, takes the colors only; the glass of a window is dense enough to read on.
        string glass = File.ReadAllText(Path.Combine(project, "Assets", "Themes", "Other", "Modern Glass.xaml"));
        int dialogStyle = glass.IndexOf("x:Key=\"PropertiesWindow\"", StringComparison.Ordinal);
        if (dialogStyle < 0 || glass.IndexOf("<shell:WindowChrome CaptionHeight=", dialogStyle, StringComparison.Ordinal) < 0 ||
            glass.IndexOf("Command=\"{x:Static SystemCommands.CloseWindowCommand}\"", dialogStyle, StringComparison.Ordinal) < 0 ||
            !glass.Contains("x:Key=\"OwnTitleWindow\"") ||
            !File.ReadAllText(Path.Combine(project, "Assets", "Themes", "Core", "System.xaml")).Contains("x:Key=\"OwnTitleWindow\"") ||
            !File.ReadAllText(Path.Combine(project, "UI", "Clock", "Calendar", "Window", "CalendarWindow.xaml")).Contains("Style=\"{DynamicResource OwnTitleWindow}\"") ||
            !File.ReadAllText(Path.Combine(project, "Core", "Appearance", "Themes", "ModernTheme.cs")).Contains("SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(window)"))
            throw new Exception("Modern Glass dialogs draw their own title bar (with a working close button); the calendar keeps its own.");
        // Only Modern Glass ships, dark and light; the light one (slate-200 with dark text, not white) is the main theme.
        var shipped = Directory.GetFiles(Path.Combine(project, "Assets", "Themes"), "*.xaml", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(name => name).ToArray();
        if (!shipped.SequenceEqual(new[] { "Modern Glass Light.xaml", "Modern Glass.xaml", "System.xaml" }))
            throw new Exception("Only the base and the two Modern Glass themes ship: " + string.Join(", ", shipped));
        string lightTheme = File.ReadAllText(Path.Combine(project, "Assets", "Themes", "Other", "Modern Glass Light.xaml"));
        if (!lightTheme.Contains("<SolidColorBrush x:Key=\"ModernBase\" Color=\"#E2E8F0\" />") || !lightTheme.Contains("<SolidColorBrush x:Key=\"ModernText\" Color=\"#1E293B\" />") ||
            !File.ReadAllText(Path.Combine(project, "Core", "Infrastructure", "Configuration", "Settings.cs")).Contains("private string _theme = \"Modern Glass Light\";") ||
            UltraWinBar.Utilities.ModernTheme.LightThemeName != "Modern Glass Light")
            throw new Exception("Modern Glass Light is light gray with dark text and is the theme of a new install.");
        // The light theme makes the panel's text heavier (no ClearType on the layered panel) and the tray a glass layer.
        if (!lightTheme.Contains("<FontWeight x:Key=\"PanelFontWeight\">SemiBold</FontWeight>") ||
            !File.ReadAllText(Path.Combine(project, "Assets", "Resources", "Brand", "FlatControls.xaml")).Contains("TextElement.FontWeight=\"{DynamicResource PanelFontWeight}\"") ||
            !TaskLabelHasPanelWeight(File.ReadAllText(Path.Combine(project, "Assets", "Themes", "Core", "System.xaml"))) ||
            !File.ReadAllText(Path.Combine(project, "Core", "Appearance", "Themes", "ModernTheme.cs")).Contains("Put(resources, \"TrayBackground\", WithAlpha(light, 0x1A));"))
            throw new Exception("The light theme's panel text is heavier and the tray's icons lie on glass.");
        string baseTheme = File.ReadAllText(Path.Combine(project, "Assets", "Themes", "Core", "System.xaml"));
        int clockStyle = baseTheme.IndexOf("x:Key=\"Clock\"", StringComparison.Ordinal);
        int tabular = clockStyle < 0 ? -1 : baseTheme.IndexOf("Typography.NumeralAlignment\" Value=\"Tabular\"", clockStyle, StringComparison.Ordinal);
        int autoRendering = clockStyle < 0 ? -1 : baseTheme.IndexOf("TextOptions.TextRenderingMode\" Value=\"Auto\"", clockStyle, StringComparison.Ordinal);
        if (clockStyle < 0 || tabular < 0 || tabular - clockStyle > 600 || (autoRendering > 0 && autoRendering - clockStyle < 400) ||
            !glass.Contains("<Setter Property=\"FontWeight\" Value=\"{DynamicResource PanelFontWeight}\" />") || !lightTheme.Contains("<Setter Property=\"FontWeight\" Value=\"{DynamicResource PanelFontWeight}\" />"))
            throw new Exception("The clock renders as the task labels with digits of one width; the windows take the panel's weight.");
        // Regression: a theme change removed every theme dictionary before adding the new base, and the show-desktop
        // button's FindResource in that gap threw "resource not found" (a crash dialog).
        string manager = File.ReadAllText(Path.Combine(project, "Core", "Appearance", "DictionaryManager.cs"));
        string showDesktop = File.ReadAllText(Path.Combine(project, "UI", "Tray", "Flyouts", "ShowDesktopButton.xaml.cs"));
        int added = manager.IndexOf("GetMergedDictionaries().Add(loaded);", StringComparison.Ordinal);
        int removed = manager.IndexOf("foreach (var old in previous) GetMergedDictionaries().Remove(old);", StringComparison.Ordinal);
        if (added < 0 || removed < added || manager.Contains("ClearPreviousThemes()") ||
            showDesktop.Contains("FindResource(\"ShowDesktopIconImage") || !showDesktop.Contains("SetResourceReference(Image.SourceProperty"))
            throw new Exception("A theme change adds the new base before removing the old themes; the show-desktop icon is a resource reference.");
        // Regression: a language switch removed every language dictionary before adding the new ones, and windows open at
        // that moment were left with empty labels. The new language now comes in before the previous one goes.
        int setLanguage = manager.IndexOf("public void SetLanguageFromSettings()", StringComparison.Ordinal);
        int keepPrevious = manager.IndexOf("var previous = GetMergedDictionaries().Where(IsLanguageDictionary).ToArray();", setLanguage, StringComparison.Ordinal);
        int loadNew = manager.IndexOf("LoadLanguageFromSettings();", setLanguage, StringComparison.Ordinal);
        int removeOld = manager.IndexOf("foreach (var old in previous) GetMergedDictionaries().Remove(old);", setLanguage, StringComparison.Ordinal);
        if (setLanguage < 0 || keepPrevious < setLanguage || loadNew < keepPrevious || removeOld < loadNew || manager.Contains("ClearPreviousLanguages"))
            throw new Exception("A language switch adds the new language before removing the previous one.");
        // On a vertical panel each zone of the clock and its dates are framed in the accent; the tray, Start and the language are not.
        string ui = Path.Combine(project, "UI");
        string clockXaml = File.ReadAllText(Path.Combine(ui, "Clock", "Clock.xaml"));
        if (File.ReadAllText(Path.Combine(ui, "Input", "InputLanguage.xaml")).Contains("BorderBrush=\"{DynamicResource ModernAccent}\"") ||
            !clockXaml.Contains("x:Key=\"HebrewClockVerticalTemplate\"") ||
            !File.ReadAllText(Path.Combine(ui, "Clock", "Clock.xaml.cs")).Contains("\"HebrewClockVerticalTemplate\""))
            throw new Exception("Only the vertical clock's blocks are framed in the accent.");
        if (UltraWinBar.Utilities.ModernTheme.WindowGlassAlpha < 0xE0)
            throw new Exception("The glass of a window is too thin to read buttons on over a white page.");
        string[] files =
        {
            Path.Combine(project, "Assets", "Themes", "Other", "Modern Glass.xaml"),
            Path.Combine(project, "Assets", "Themes", "Other", "Modern Glass Light.xaml"),
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
        if (!File.ReadAllText(files[0]).Contains("<system:Boolean x:Key=\"AllowsTransparency\">True</system:Boolean>"))
            throw new Exception("The panel of Modern Glass must be the layered window that the blur behind it needs.");
        Console.WriteLine("PASS: Modern Glass is plain brushes: no effects, animations, gradients or animated popups; the only layered window is the panel's.");

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
        if (!theme.Contains("x:Key=\"ListRowHover\" Color=\"#2622D3EE\"") || !theme.Contains("x:Key=\"ListRowHoverEdge\" Color=\"#6622D3EE\""))
            throw new Exception("The rows of the calendar do not light up in the cyan accent by default.");
        // The surfaces of the dark theme (the default is the light one now): checked with the dark theme chosen.
        string chosenTheme = Settings.Instance.Theme;
        Settings.Instance.Theme = ModernTheme.ThemeName;
        try
        {
        var cyan = ModernTheme.SurfacesOf("cyan");
        foreach (var (key, color) in new[] { ("ModernBase", cyan.Base), ("ModernBaseDeep", cyan.Deep), ("ModernRaised", cyan.Raised), ("ButtonFace", cyan.Panel) })
            if (!theme.Contains($"x:Key=\"{key}\" Color=\"#{Hex(color)}\"")) throw new Exception($"The theme's default {key} is not the cyan-tinted #{Hex(color)}.");
        var bases = new System.Collections.Generic.HashSet<string>();
        foreach (var (name, _, _) in Tailwind)
        {
            var surfaces = ModernTheme.SurfacesOf(name);
            if (!bases.Add(Hex(surfaces.Base))) throw new Exception("The accents " + name + " and another give the same window color: the accent would not show.");
            // Text on the tinted surfaces: normal text on the raised layer, muted text on the window.
            if (Contrast(Color.FromRgb(0xE2, 0xE8, 0xF0), surfaces.Raised) < 7 || Contrast(Color.FromRgb(0x94, 0xA3, 0xB8), surfaces.Base) < 4.5)
                throw new Exception($"Text on the {name}-tinted surfaces is too faint.");
        }
        }
        finally
        {
            Settings.Instance.Theme = chosenTheme;
        }

        var settings = new Settings();
        if (settings.Theme != ModernTheme.LightThemeName || settings.AccentColor != "cyan")
            throw new Exception("A new installation does not start with Modern Glass Light in cyan.");
        settings.AccentColor = "not-a-color";
        if (settings.AccentColor != "cyan") throw new Exception("An unknown accent in the settings file is not turned into cyan.");
        settings.AccentColor = "rose";
        if (settings.AccentColor != "rose") throw new Exception("A known accent was not kept.");
        Console.WriteLine("PASS: the six accents are the Tailwind colors, readable under dark text; Modern Glass Light in cyan is the default and a wrong accent falls back to cyan.");
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
                var surfaces = ModernTheme.SurfacesOf(name);
                CheckBrush(app, "ModernBase", Hex(surfaces.Base));
                CheckBrush(app, "ModernRaised", Hex(surfaces.Raised));
                CheckBrush(app, "ButtonFace", Hex(surfaces.Panel));
                if (app.Resources["ModernGlassBorder"] is not SolidColorBrush edge || edge.Color.A != 0x40 || Hex(edge.Color) != light)
                    throw new Exception("The hairlines of " + name + " are not the accent at 25 percent.");
                // The window is translucent over the blurred desktop when the blur is on and the system has it, and opaque otherwise.
                if (app.Resources["ModernWindowFill"] is not SolidColorBrush fill || Hex(fill.Color) != Hex(surfaces.Base) ||
                    fill.Color.A != (ModernBackdrop.Wanted ? ModernTheme.WindowGlassAlpha : 0xFF))
                    throw new Exception("The window fill of " + name + " is not the tinted base at the right opacity.");
                ModernTheme.ApplyPanel();
                if (app.Resources["FlatPanelFill"] is not SolidColorBrush panelFill) throw new Exception("The panel has no fill brush.");
                if (ModernBackdrop.PanelWanted ? panelFill.Color.A != ModernTheme.PanelGlassAlpha || Hex(panelFill.Color) != Hex(surfaces.Panel) : panelFill.Color.A != 0xFF)
                    throw new Exception("The panel fill of " + name + " is not the tinted panel at the right opacity.");
                if (ModernBackdrop.PanelWanted && (app.Resources["FlatRest"] is not SolidColorBrush { Color.A: 0 } || app.Resources["FlatTask"] is not SolidColorBrush { Color.A: 0x2E }))
                    throw new Exception("With the blur on the panel's buttons are not layers of the accent.");
                if (app.Resources["ListRowHover"] is not SolidColorBrush rowFill || rowFill.Color.A != 0x26 || Hex(rowFill.Color) != light ||
                    app.Resources["ListRowHoverEdge"] is not SolidColorBrush rowEdge || rowEdge.Color.A != 0x66 || Hex(rowEdge.Color) != light)
                    throw new Exception("The row under the pointer is not the accent " + name + ".");
                var box = new CheckBox { IsChecked = true, Content = "Box" };
                var pixel = Render(box, out double height, 8, (int)((Math.Ceiling(Layout(box).Height) - 16) / 2) + 2);
                if (Math.Abs(pixel.R - Convert.ToInt32(light.Substring(0, 2), 16)) > 3 || Math.Abs(pixel.G - Convert.ToInt32(light.Substring(2, 2), 16)) > 3 ||
                    Math.Abs(pixel.B - Convert.ToInt32(light.Substring(4, 2), 16)) > 3)
                    throw new Exception($"A checked box is {pixel.R:X2}{pixel.G:X2}{pixel.B:X2} under the {name} accent, expected {light}.");
            }
            settings.Theme = "System";
            ModernTheme.Apply();
            if (app.Resources.Contains("ModernAccent") || app.Resources.Contains("ModernBase") || app.Resources.Contains("ButtonFace") || app.Resources.Contains(SystemColors.HighlightBrushKey))
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
