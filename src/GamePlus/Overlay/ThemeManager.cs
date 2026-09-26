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

    /// <param name="panelOpacity">Opacity of the overlay panel in percent (only the panel background, never the text).</param>
    public static void Apply(string? theme, int panelOpacity)
    {
        var name = Normalize(theme) == Light ? "Light" : "Dark";
        var resources = System.Windows.Application.Current.Resources;
        var dictionaries = resources.MergedDictionaries;

        var current = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Colors.") == true);
        if (current is null || !current.Source!.OriginalString.EndsWith($"Colors.{name}.xaml"))
        {
            var index = current is null ? 0 : dictionaries.IndexOf(current);
            if (current is not null) dictionaries.Remove(current);
            current = new ResourceDictionary { Source = new Uri($"Overlay/Themes/Colors.{name}.xaml", UriKind.Relative) };
            dictionaries.Insert(index, current);
        }

        // The panel color comes from the theme; only its alpha is the user's. An entry on the app itself shadows the merged one.
        var panel = ((SolidColorBrush)current["PanelBrush"]).Color;
        var alpha = (byte)Math.Round(Math.Clamp(panelOpacity, 0, 100) * 255 / 100.0);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, panel.R, panel.G, panel.B));
        brush.Freeze();
        resources["PanelBrush"] = brush;
    }
}
