using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Every declared language shows the whole UI in itself: each English key is translated, with the same placeholders
// and the same number of list items, and no key that English has not. British English only overrides a few words.
internal static class LanguageChecks
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    // Not words: the text direction and the Start button's font stay as English has them unless a language needs others.
    private static readonly HashSet<string> Technical = new() { "flow_direction", "start_text_xp_font_family" };

    internal static void Run(string[] args, DirectoryInfo repositoryRoot)
    {
        string languages = Path.Combine(repositoryRoot.FullName, "UltraWinBar", "Assets", "Languages");
        var english = Load(Path.Combine(languages, "Group01", "English.xaml"));
        var files = Directory.GetFiles(languages, "*.xaml", SearchOption.AllDirectories)
            .Where(file => Path.GetFileNameWithoutExtension(file) is not ("English" or "English (United Kingdom)")).ToList();
        if (files.Count < 40) throw new Exception($"Only {files.Count} languages found.");
        var problems = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var strings = Load(file);
            foreach (var (key, value) in english)
            {
                if (!strings.TryGetValue(key, out var translated))
                {
                    if (!Technical.Contains(key)) problems.Add($"{name}: {key} missing");
                    continue;
                }
                if (value.Items != translated.Items) problems.Add($"{name}: {key} has {translated.Items} items, English {value.Items}");
                else if (Placeholders(value.Text) != Placeholders(translated.Text)) problems.Add($"{name}: {key} placeholders differ");
            }
            problems.AddRange(strings.Keys.Where(key => !english.ContainsKey(key)).Select(key => $"{name}: {key} is not an English key"));
        }
        if (problems.Count > 0)
            throw new Exception($"{problems.Count} translation problems: " + string.Join("; ", problems.Take(20)));
        Console.WriteLine($"PASS: {files.Count} languages translate every English key, with its placeholders and list items.");
    }

    // A key's text (the items of a list joined) and its number of list items (0 for a single string).
    private static Dictionary<string, (string Text, int Items)> Load(string file) =>
        XDocument.Load(file).Root.Elements()
            .Where(element => element.Attribute(X + "Key") != null)
            .ToDictionary(element => element.Attribute(X + "Key").Value,
                element => element.HasElements
                    ? (string.Join("\n", element.Elements().Select(item => item.Value)), element.Elements().Count())
                    : (element.Value, 0));

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{\d+[^}]*\}").Select(match => match.Value).OrderBy(value => value, StringComparer.Ordinal));
}
