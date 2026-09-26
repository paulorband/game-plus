using System.Globalization;

namespace GamePlus.Models;

/// <summary>Named colors for the on-screen display, stored as "RRGGBB" (the format RTSS's &lt;C=...&gt; tag takes). Keys are language-neutral; the overlay shows preset.&lt;key&gt; from the language file.</summary>
public static class ColorPresets
{
    public const string DefaultLabel = "35D8A0";
    public const string DefaultValue = "FFFFFF";

    public static readonly (string Key, string Hex)[] All =
    [
        ("green", "35D8A0"),
        ("white", "FFFFFF"),
        ("gray", "C8CCD4"),
        ("cyan", "4CC9F0"),
        ("blue", "4D8DFF"),
        ("purple", "B48CFF"),
        ("pink", "FF6BB5"),
        ("red", "FF4D4D"),
        ("orange", "FF9F1C"),
        ("yellow", "FFD60A"),
    ];

    /// <summary>Uppercase 6-digit hex, or <paramref name="fallback"/> when the text isn't one.</summary>
    public static string Normalize(string? hex, string fallback)
    {
        hex = hex?.Trim().TrimStart('#');
        return hex is { Length: 6 } && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? hex.ToUpperInvariant() : fallback;
    }

    public static string? NameOf(string hex) =>
        All.Where(p => p.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).FirstOrDefault();

    public static (int R, int G, int B) Split(string hex) => (
        Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));

    public static string Join(int r, int g, int b) => $"{r:X2}{g:X2}{b:X2}";
}
