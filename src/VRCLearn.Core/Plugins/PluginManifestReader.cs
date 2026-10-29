using System.Text;
using System.Text.Json;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins;

public static class PluginManifestReader
{
    public const string FileName = "plugin.json";

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static PluginManifest Read(string json)
    {
        ManifestDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new PluginManifestException($"plugin.json is not valid JSON: {exception.Message}", exception);
        }

        if (dto is null)
        {
            throw new PluginManifestException("plugin.json is empty");
        }

        var window = dto.Window;

        var manifest = new PluginManifest
        {
            SchemaVersion = dto.SchemaVersion,
            Id = dto.Id ?? "",
            Name = dto.Name ?? PluginLocalizedText.Empty,
            Description = dto.Description ?? PluginLocalizedText.Empty,
            Version = PluginManifestValidator.ParseVersion(dto.Version ?? "", "version"),
            Author = dto.Author ?? "",
            ApiVersion = dto.ApiVersion,
            MinHostVersion = dto.MinHostVersion is { Length: > 0 } minHost
                ? PluginManifestValidator.ParseVersion(minHost, "minHostVersion")
                : null,
            Entry = new PluginEntryPoint(dto.Entry?.Assembly ?? "", dto.Entry?.Type ?? ""),
            Icon = dto.Icon,
            Window = new PluginWindowOptions(
                window is { Width: > 0 } ? window.Width : 720,
                window is { Height: > 0 } ? window.Height : 540,
                window?.Resizable ?? true),
            Provides = [.. (dto.Provides ?? []).Select(x => new PluginCapability(x.Capability ?? "", x.Version))],
            Requires = [.. (dto.Requires ?? []).Select(x => new PluginRequirement(x.Capability ?? "", x.MinVersion))],
        };

        PluginManifestValidator.Validate(manifest);

        return manifest;
    }

    public static PluginManifest Read(Stream stream)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        return Read(reader.ReadToEnd());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Compact);
        options.Converters.Add(new PluginLocalizedTextConverter());
        return options;
    }

    private sealed class ManifestDto
    {
        public int SchemaVersion { get; set; }

        public string? Id { get; set; }

        public PluginLocalizedText? Name { get; set; }

        public PluginLocalizedText? Description { get; set; }

        public string? Version { get; set; }

        public string? Author { get; set; }

        public int ApiVersion { get; set; }

        public string? MinHostVersion { get; set; }

        public EntryDto? Entry { get; set; }

        public string? Icon { get; set; }

        public WindowDto? Window { get; set; }

        public List<CapabilityDto>? Provides { get; set; }

        public List<RequirementDto>? Requires { get; set; }
    }

    private sealed class EntryDto
    {
        public string? Assembly { get; set; }

        public string? Type { get; set; }
    }

    private sealed class WindowDto
    {
        public double Width { get; set; }

        public double Height { get; set; }

        public bool Resizable { get; set; } = true;
    }

    private sealed class CapabilityDto
    {
        public string? Capability { get; set; }

        public int Version { get; set; }
    }

    private sealed class RequirementDto
    {
        public string? Capability { get; set; }

        public int MinVersion { get; set; }
    }
}
