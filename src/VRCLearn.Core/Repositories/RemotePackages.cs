using System.Text.Json;
using System.Text.Json.Nodes;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Serialization;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Repositories;

public sealed class RemotePackages
{
    private readonly Dictionary<PackageVersion, PackageManifest> _versions;

    internal RemotePackages(Dictionary<PackageVersion, PackageManifest> versions) => _versions = versions;

    public IEnumerable<PackageManifest> AllVersions => _versions.Values;

    internal static RemotePackages Parse(JsonObject packageObject)
    {
        var versions = new Dictionary<PackageVersion, PackageManifest>();

        if (packageObject.TryGetPropertyValue("versions", out var versionsNode))
        {
            if (versionsNode is not JsonObject versionsObject)
            {
                throw new RepositoryParseException("versions must be an object");
            }

            foreach (var (versionText, manifestNode) in versionsObject)
            {
                if (!PackageVersion.TryParse(versionText, out var version))
                {
                    continue;
                }

                if (TryParseManifest(manifestNode) is { } manifest)
                {
                    versions[version] = manifest;
                }
            }
        }

        return new RemotePackages(versions);
    }

    public PackageManifest? GetVersion(PackageVersion version) => _versions.GetValueOrDefault(version);

    public PackageManifest? GetLatest(VersionSelector selector) =>
        selector.AsSpecificVersion() is { } specific
            ? _versions.GetValueOrDefault(specific)
            : LatestMatching(selector);

    private PackageManifest? LatestMatching(VersionSelector selector)
    {
        PackageManifest? latest = null;

        foreach (var manifest in _versions.Values)
        {
            if (selector.Satisfies(manifest) && (latest is null || manifest.Version.CompareTo(latest.Version) > 0))
            {
                latest = manifest;
            }
        }

        return latest;
    }

    private static PackageManifest? TryParseManifest(JsonNode? node)
    {
        if (node is not JsonObject manifestObject)
        {
            return null;
        }

        try
        {
            return manifestObject.Deserialize<PackageManifest>(VpmJson.Options);
        }
        catch (Exception exception) when (exception is JsonException or VersionParseException)
        {
            return null;
        }
    }
}
