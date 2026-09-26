using System.Windows;
using System.Windows.Media;

namespace GamePlus.Overlay;

/// <summary>Swaps the color dictionary of the running app. Every control reads brushes through DynamicResource,
/// so replacing the dictionary restyles open windows immediately.</summary>
public static class ThemeManager
{
    public const string Dark = "dark";
    public const string Light = "light";

    public static string Normalize(string? theme) => theme == Light ? Light : Dark;

    public static void Apply(string? theme)
    {
        var name = Normalize(theme) == Light ? "Light" : "Dark";
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;

        var current = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Colors.") == true);
        var index = current is null ? 0 : dictionaries.IndexOf(current);
        if (current is not null && current.Source!.OriginalString.EndsWith($"Colors.{name}.xaml")) return;
        if (current is not null) dictionaries.Remove(current);

        dictionaries.Insert(index, new ResourceDictionary { Source = new Uri($"Overlay/Themes/Colors.{name}.xaml", UriKind.Relative) });
    }
}
