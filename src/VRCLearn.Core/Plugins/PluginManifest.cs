using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins;

public sealed class PluginManifest
{
    public int SchemaVersion { get; init; }

    public string Id { get; init; } = "";

    public PluginLocalizedText Name { get; init; } = PluginLocalizedText.Empty;

    public PluginLocalizedText Description { get; init; } = PluginLocalizedText.Empty;

    public PackageVersion Version { get; init; } = new(0, 0, 0);

    public string Author { get; init; } = "";

    public int ApiVersion { get; init; }

    public PackageVersion? MinHostVersion { get; init; }

    public PluginEntryPoint Entry { get; init; } = new("", "");

    public string? Icon { get; init; }

    public PluginWindowOptions Window { get; init; } = new();

    public IReadOnlyList<PluginCapability> Provides { get; init; } = [];

    public IReadOnlyList<PluginRequirement> Requires { get; init; } = [];
}

public sealed record PluginEntryPoint(string Assembly, string Type);

public sealed record PluginWindowOptions(double Width = 720, double Height = 540, bool Resizable = true);

public sealed record PluginCapability(string Capability, int Version);

public sealed record PluginRequirement(string Capability, int MinVersion);
