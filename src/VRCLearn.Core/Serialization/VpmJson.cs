using System.Text.Json;
using System.Text.Json.Serialization;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Serialization;

public static class VpmJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static PackageManifest ReadManifest(string json) =>
        JsonSerializer.Deserialize<PackageManifest>(json, Options)
        ?? throw new JsonException("package manifest is null");

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Indented);
        options.Converters.Add(new PackageVersionConverter());
        options.Converters.Add(new VersionRangeConverter());
        options.Converters.Add(new DependencyRangeConverter());
        options.Converters.Add(new PartialUnityVersionConverter());
        options.Converters.Add(new YankStateConverter());
        options.Converters.Add(new UriConverter());
        return options;
    }
}

internal sealed class DependencyRangeConverter : JsonConverter<DependencyRange>
{
    public override DependencyRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? DependencyRange.Parse(reader.GetString()!)
            : throw new JsonException("invalid dependency range");

    public override void Write(Utf8JsonWriter writer, DependencyRange value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal sealed class PackageVersionConverter : JsonConverter<PackageVersion>
{
    public override PackageVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && PackageVersion.TryParse(reader.GetString(), out var version)
            ? version
            : throw new JsonException("invalid version");

    public override void Write(Utf8JsonWriter writer, PackageVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal sealed class VersionRangeConverter : JsonConverter<VersionRange>
{
    public override VersionRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && VersionRange.TryParse(reader.GetString(), out var range)
            ? range
            : throw new JsonException("invalid version range");

    public override void Write(Utf8JsonWriter writer, VersionRange value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal sealed class PartialUnityVersionConverter : JsonConverter<PartialUnityVersion>
{
    public override PartialUnityVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && PartialUnityVersion.TryParse(reader.GetString()) is { } version
            ? version
            : throw new JsonException("invalid unity version");

    public override void Write(Utf8JsonWriter writer, PartialUnityVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal sealed class YankStateConverter : JsonConverter<YankState>
{
    public override YankState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => YankState.FromBoolean(true),
            JsonTokenType.False => YankState.FromBoolean(false),
            JsonTokenType.String => YankState.FromReason(reader.GetString()!),
            _ => throw new JsonException("yanked must be a boolean or a string"),
        };

    public override void Write(Utf8JsonWriter writer, YankState value, JsonSerializerOptions options)
    {
        if (value.Reason is not null)
        {
            writer.WriteStringValue(value.Reason);
        }
        else
        {
            writer.WriteBooleanValue(value.IsYanked);
        }
    }
}

/// <summary>空字符串与纯空白按 null 处理。</summary>
internal sealed class UriConverter : JsonConverter<Uri>
{
    public override Uri? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : throw new JsonException($"invalid url: {text}");
    }

    public override void Write(Utf8JsonWriter writer, Uri value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
