using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace GamePlus.Services;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// UI text lookup. Languages are flat JSON files ({ "_name": "...", "_code": "...", "key": "text" }).
/// The built-in English and Portuguese ship inside the exe; any <c>Languages\*.json</c> next to the exe is loaded on top,
/// so adding a language (or fixing a translation) is just dropping a file, with no rebuild. A file may be partial:
/// missing keys fall back to English, then to the key itself. XAML binds through the indexer, which refreshes live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private const string Fallback = "en";
    private readonly Dictionary<string, Dictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _current = new();
    private Dictionary<string, string> _fallback = new();

    public string Code { get; private set; } = Fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language actually changed.</summary>
    public event Action? LanguageChanged;

    private Loc()
    {
        LoadEmbedded();
        LoadExternal();
        if (!_languages.ContainsKey(Fallback)) _languages[Fallback] = new();
        _fallback = _languages[Fallback];
        Select(null);
    }

    public IReadOnlyList<LanguageInfo> Available =>
        _languages.Select(l => new LanguageInfo(l.Key, l.Value.GetValueOrDefault("_name", l.Key)))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public string this[string key] => Get(key);

    private string Get(string key) =>
        _current.TryGetValue(key, out var text) ? text
        : _fallback.TryGetValue(key, out text) ? text
        : key;

    /// <summary>Text for <paramref name="key"/>, formatted with <paramref name="args"/> when given.</summary>
    public static string T(string key, params object[] args)
    {
        var text = Instance.Get(key);
        if (args.Length == 0) return text;
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException) { return text; }
    }

    /// <summary>Switches language. Null or unknown follows the system UI language (exact code, then same language, then English).</summary>
    public void Select(string? code)
    {
        var chosen = Resolve(code);
        var changed = !string.Equals(chosen, Code, StringComparison.OrdinalIgnoreCase) || _current.Count == 0;
        Code = _languages.Keys.First(k => k.Equals(chosen, StringComparison.OrdinalIgnoreCase));
        _current = _languages[Code];
        if (!changed) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        LanguageChanged?.Invoke();
    }

    private string Resolve(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code) && _languages.ContainsKey(code)) return code;
        var system = CultureInfo.CurrentUICulture;
        if (_languages.ContainsKey(system.Name)) return system.Name;
        var sameLanguage = _languages.Keys.FirstOrDefault(k => k.StartsWith(system.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase)
            || k.Equals(system.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
        return sameLanguage ?? Fallback;
    }

    private void LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Languages/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            Merge(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(name));
        }
    }

    private void LoadExternal()
    {
        try
        {
            var folder = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "Languages");
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                try { Merge(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error($"Read language file {file}", ex); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Enumerate Languages folder", ex);
        }
    }

    // Layers a file over what is already known for its code, so an external file can be partial.
    private void Merge(string json, string fallbackCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var entries = doc.RootElement.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            var code = entries.GetValueOrDefault("_code", fallbackCode);
            if (!_languages.TryGetValue(code, out var target)) _languages[code] = target = new();
            foreach (var (key, text) in entries) target[key] = text;
        }
        catch (JsonException ex)
        {
            Log.Error($"Parse language file {fallbackCode}", ex);
        }
    }
}
