using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRCLearn.Core.Plugins;

public sealed class PluginLocalizedText
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public PluginLocalizedText(IReadOnlyDictionary<string, string> values) => _values = values;

    public static PluginLocalizedText Empty { get; } = new(new Dictionary<string, string>());

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool IsEmpty => _values.Count == 0;

    public string Pick(string language)
    {
        if (_values.TryGetValue(language, out var exact))
        {
            return exact;
        }

        var neutral = language.Split('-')[0];

        if (_values.FirstOrDefault(x => string.Equals(x.Key, neutral, StringComparison.OrdinalIgnoreCase)) is { Value: { } neutralValue })
        {
            return neutralValue;
        }

        if (_values.TryGetValue("en", out var english))
        {
            return english;
        }

        return _values.OrderBy(x => x.Key, StringComparer.Ordinal).FirstOrDefault().Value ?? "";
    }
}

public sealed class PluginLocalizedTextConverter : JsonConverter<PluginLocalizedText>
{
    public override PluginLocalizedText Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new PluginLocalizedText(new Dictionary<string, string>
            {
                ["en"] = reader.GetString() ?? "",
            });
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("localized text must be a string or an object");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("localized text keys must be strings");
            }

            var key = reader.GetString() ?? "";

            reader.Read();

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("localized text values must be strings");
            }

            values[key] = reader.GetString() ?? "";
        }

        return new PluginLocalizedText(values);
    }

    public override void Write(
        Utf8JsonWriter writer,
        PluginLocalizedText value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var pair in value.Values)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }
}
