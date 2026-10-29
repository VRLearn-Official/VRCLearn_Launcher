using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using VRCLearn.Core.Configuration;

namespace VRCLearn.App.Services;

/// <summary>语言包。<paramref name="Code"/> 是文件名（不带 .json）。</summary>
public sealed record LanguagePack(string Code, string Display);

public sealed class LocalizationService : INotifyPropertyChanged
{
    /// <summary>语言包中标注语言名的键。</summary>
    public const string LanguageKey = "@language";

    private const string FolderName = "Localization";
    private const string FallbackCode = "zh-Hans";

    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();

    public LocalizationService()
    {
        Instance = this;
        Packs = Discover();
        Apply("");
    }

    public static LocalizationService Instance { get; private set; } = null!;

    /// <summary>Localization 目录里所有的语言包。内置的排在前面。</summary>
    public IReadOnlyList<LanguagePack> Packs { get; }

    public string CurrentLanguage { get; private set; } = "";

    public string this[string key] => _strings.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, this[key], arguments);

    public string FormatDiskSpace(InsufficientDiskSpaceException exception) =>
        Format(
            "DiskSpace.NotEnough",
            SizeText.Format(exception.RequiredBytes),
            SizeText.Format(exception.AvailableBytes));

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(string language)
    {
        CurrentLanguage = Resolve(language);
        _strings = Read(Path.Combine(Folder, CurrentLanguage + ".json"));

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static string Folder => Path.Combine(AppContext.BaseDirectory, FolderName);

    private string Resolve(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            var culture = CultureInfo.CurrentUICulture;

            return Find(culture.Name) ?? Find(culture.TwoLetterISOLanguageName) ?? Fallback();
        }

        return Find(language) ?? Fallback();
    }

    private string? Find(string code) =>
        Packs.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))?.Code;

    private string Fallback() =>
        Find(FallbackCode) ?? Packs.FirstOrDefault()?.Code ?? FallbackCode;

    private static IReadOnlyList<LanguagePack> Discover()
    {
        if (!System.IO.Directory.Exists(Folder))
        {
            return [];
        }

        return
        [
            .. System.IO.Directory.EnumerateFiles(Folder, "*.json")
                .Select(path => new LanguagePack(Path.GetFileNameWithoutExtension(path), Display(path)))
                .OrderBy(x => x.Code.Equals(FallbackCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static string Display(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path)).GetValueOrDefault(LanguageKey) is { Length: > 0 } name
                ? name
                : Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }

    private static IReadOnlyDictionary<string, string> Read(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return Parse(File.ReadAllText(path))
                .Where(x => !x.Key.StartsWith('@'))
                .ToDictionary(x => x.Key, x => x.Value);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static Dictionary<string, string> Parse(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.Compact) ?? [];
}
