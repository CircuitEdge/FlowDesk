using System.IO;
using System.Text.Json;

namespace FlowDeck.Services;

internal sealed class FlowDeckStorageService
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowDeck");

    public string LanguagePath => Path.Combine(_directory, "language.txt");
    public string ThemePath => Path.Combine(_directory, "theme.txt");

    public string? LoadLanguage() => LoadText(LanguagePath);
    public string? LoadTheme() => LoadText(ThemePath);

    public void SaveLanguage(string language) => SaveText(LanguagePath, language);
    public void SaveTheme(string theme) => SaveText(ThemePath, theme);

    public Dictionary<string, int>? LoadAppUsage() =>
        LoadJson<Dictionary<string, int>>(Path.Combine(_directory, "app-usage.json"));

    public void SaveAppUsage(IReadOnlyDictionary<string, int> usage) =>
        SaveJson(Path.Combine(_directory, "app-usage.json"), usage);

    public List<string>? LoadNotes() => LoadJson<List<string>>(Path.Combine(_directory, "notes.json"));

    public void SaveNotes(IReadOnlyCollection<string> notes) =>
        SaveJson(Path.Combine(_directory, "notes.json"), notes);

    public ClipboardPrivacySettings? LoadClipboardPrivacySettings() =>
        LoadJson<ClipboardPrivacySettings>(Path.Combine(_directory, "clipboard-settings.json"));

    public void SaveClipboardPrivacySettings(ClipboardPrivacySettings settings) =>
        SaveJson(Path.Combine(_directory, "clipboard-settings.json"), settings);

    private static string? LoadText(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    private void SaveText(string path, string value)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, value);
    }

    private static T? LoadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;

    private void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }
}
