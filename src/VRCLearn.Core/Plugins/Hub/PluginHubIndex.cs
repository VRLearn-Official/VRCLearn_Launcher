using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins.Hub;

public sealed class PluginHubIndex
{
    public int SchemaVersion { get; init; }

    public IReadOnlyList<PluginHubEntry> Plugins { get; init; } = [];
}

public sealed class PluginHubEntry
{
    public string Id { get; init; } = "";

    public int ApiVersion { get; init; }

    public PackageVersion? MinHostVersion { get; init; }

    public IReadOnlyList<PluginHubRelease> Versions { get; init; } = [];

    public PluginHubRelease? SelectLatestRelease()
    {
        PluginHubRelease? latest = null;

        foreach (var release in Versions)
        {
            if (latest is null || release.Version > latest.Version)
            {
                latest = release;
            }
        }

        return latest;
    }
}

public sealed class PluginHubRelease
{
    public PackageVersion Version { get; init; } = new(0, 0, 0);

    public long Size { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public string Url { get; init; } = "";

    public string? Sha256 { get; init; }
}

public sealed record PluginHubTarget(string PluginId, PackageVersion Version, string Hub);

public sealed record PluginHubUpdateCandidate(
    string PluginId,
    string Hub,
    PackageVersion Version,
    Uri PackageUri,
    string? Sha256,
    long Size);
