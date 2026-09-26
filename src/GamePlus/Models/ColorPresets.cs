using System.Globalization;

namespace GamePlus.Models;

/// <summary>Named colors for the on-screen display, stored as "RRGGBB" (the format RTSS's &lt;C=...&gt; tag takes).</summary>
public static class ColorPresets
{
    public const string CustomName = "Personalizada";
    public const string DefaultLabel = "35D8A0";
    public const string DefaultValue = "FFFFFF";

    public static readonly (string Name, string Hex)[] All =
    [
        ("Verde", "35D8A0"),
        ("Branco", "FFFFFF"),
        ("Cinza", "C8CCD4"),
        ("Ciano", "4CC9F0"),
        ("Azul", "4D8DFF"),
        ("Roxo", "B48CFF"),
        ("Rosa", "FF6BB5"),
        ("Vermelho", "FF4D4D"),
        ("Laranja", "FF9F1C"),
        ("Amarelo", "FFD60A"),
    ];

    /// <summary>Uppercase 6-digit hex, or <paramref name="fallback"/> when the text isn't one.</summary>
    public static string Normalize(string? hex, string fallback)
    {
        hex = hex?.Trim().TrimStart('#');
        return hex is { Length: 6 } && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? hex.ToUpperInvariant() : fallback;
    }

    public static string? NameOf(string hex) =>
        All.Where(p => p.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).FirstOrDefault();

    public static (int R, int G, int B) Split(string hex) => (
        Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));

    public static string Join(int r, int g, int b) => $"{r:X2}{g:X2}{b:X2}";
}
