using System.IO;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

internal sealed class PluginLocalization : IPluginLocalization
{
    private readonly string _localizationDirectory;
    private readonly LocalizationService _host;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _cache = [];

    public PluginLocalization(string pluginDirectory, LocalizationService host, ILogger logger)
    {
        _localizationDirectory = Path.Combine(pluginDirectory, "localization");
        _host = host;
        _logger = logger;
    }

    public string Language => _host.CurrentLanguage;

    public string this[string key] => Get(key);

    public string Format(string key, params object?[] arguments)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(exception, "插件文案格式错误：{Key}", key);
            return Get(key);
        }
    }

    public void Reload() => _cache.Clear();

    private string Get(string key) =>
        Load(Language).TryGetValue(key, out var value) ? value : key;

    private IReadOnlyDictionary<string, string> Load(string language)
    {
        if (_cache.TryGetValue(language, out var cached))
        {
            return cached;
        }

        foreach (var candidate in Candidates(language))
        {
            var path = Path.Combine(_localizationDirectory, $"{candidate}.json");

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(path),
                    JsonDefaults.Compact);

                if (values is not null)
                {
                    _cache[language] = values;
                    return values;
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                _logger.LogWarning(exception, "读取插件文案失败：{Path}", path);
            }
        }

        _cache[language] = new Dictionary<string, string>();
        return _cache[language];
    }

    private static IEnumerable<string> Candidates(string language)
    {
        yield return language;

        var neutral = language.Split('-')[0];

        if (!string.Equals(neutral, language, StringComparison.OrdinalIgnoreCase))
        {
            yield return neutral;
        }

        if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            yield return "en";
        }
    }
}
