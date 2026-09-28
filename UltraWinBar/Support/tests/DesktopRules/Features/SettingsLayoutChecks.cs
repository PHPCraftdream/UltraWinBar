using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

internal static class SettingsLayoutChecks
{
    internal static void Check()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Layout", "PropertiesWindow.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        if ((string)xml.Root.Attribute("SizeToContent") != "Manual") throw new Exception("Settings size follows theme contents.");
        var outerTabsXml = xml.Descendants(ui + "TabControl").First().Elements(ui + "TabItem").ToArray();
        var innerTabsXml = outerTabsXml[0].Element(ui + "TabControl")?.Elements(ui + "TabItem").ToArray();
        if (innerTabsXml == null || innerTabsXml.Length != 5 ||
            innerTabsXml.Any(tab => tab.Elements().FirstOrDefault()?.Name != ui + "ScrollViewer") ||
            outerTabsXml.Skip(1).Any(tab => tab.Elements().FirstOrDefault()?.Name != ui + "ScrollViewer"))
            throw new Exception("Settings page has no bounded scrolling.");
        if (xml.Descendants(ui + "ComboBox").Any(combo => combo.Parent.Name != ui + "Grid"))
            throw new Exception("Settings combo is outside a grid row.");
        foreach (var element in xml.Descendants().ToArray())
        {
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    if (attribute.Value.StartsWith("clr-namespace:")) attribute.Value += ";assembly=UltraWinBar";
                    continue;
                }
                if (attribute.Name.LocalName == "Class" || attribute.Value.Contains("Settings.Instance") ||
                    Regex.IsMatch(attribute.Value, "^[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+$")) attribute.Remove();
            }
            if (element.Name.NamespaceName.StartsWith("clr-namespace:UltraWinBar.Controls"))
                element.ReplaceWith(new XElement(ui + "Border", new XAttribute("Width", 32), new XAttribute("Height", 24)));
            else if (element.Name.NamespaceName.StartsWith("clr-namespace:UltraWinBar.Converters"))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=UltraWinBar");
        }
        xml.Root.Name = ui + "UserControl";
        foreach (string name in new[] { "Title", "Icon", "Height", "Width", "MinWidth", "MinHeight", "ResizeMode", "SizeToContent", "Style" })
            xml.Root.Attribute(name)?.Remove();
        xml.Root.Element(ui + "Window.Resources").Name = ui + "UserControl.Resources";
        xml.Root.Element(ui + "UserControl.Resources").Element(ui + "ResourceDictionary")
            .SetAttributeValue("Source", "pack://application:,,,/UltraWinBar;component/Shell/Dialogs/SettingsStyles.xaml");
        var control = (UserControl)XamlReader.Parse(xml.ToString());
        var tabs = Descendants(control).OfType<TabControl>().FirstOrDefault();
        control.Measure(new Size(640, 700));
        control.Arrange(new Rect(0, 0, 640, 700));
        control.UpdateLayout();
        tabs ??= Descendants(control).OfType<TabControl>().First();
        var innerTabs = Descendants(control).OfType<TabControl>().Skip(1).First();
        int checkedCombos = 0;
        foreach (double width in new[] { 360d, 640d })
        {
            tabs.SelectedIndex = 0;
            for (int tab = 0; tab < innerTabs.Items.Count; tab++)
            {
                innerTabs.SelectedIndex = tab;
                control.Measure(new Size(width, 600));
                control.Arrange(new Rect(0, 0, width, 600));
                control.UpdateLayout();
                checkedCombos += CheckCombos(control, width);
            }

            for (int tab = 1; tab < tabs.Items.Count; tab++)
            {
                tabs.SelectedIndex = tab;
                control.Measure(new Size(width, 600));
                control.Arrange(new Rect(0, 0, width, 600));
                control.UpdateLayout();
                checkedCombos += CheckCombos(control, width);
            }
        }
        if (checkedCombos == 0) throw new Exception("Settings layout test did not measure any combo boxes.");
    }

    private static int CheckCombos(UserControl control, double width)
    {
        int checkedCombos = 0;
        foreach (var combo in Descendants(control).OfType<ComboBox>())
        {
            if (combo.ActualWidth == 0) continue;
            var point = combo.TranslatePoint(new Point(), control);
            if (point.X < -1 || point.X + combo.ActualWidth > width + 1)
                throw new Exception($"Settings combo overflows: {combo.Name}, width={width}, x={point.X}, controlWidth={combo.ActualWidth}");
            checkedCombos++;
        }
        return checkedCombos;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
