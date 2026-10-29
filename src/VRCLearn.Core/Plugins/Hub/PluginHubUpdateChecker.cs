using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins.Hub;

public static class PluginHubUpdateChecker
{
    public static PluginHubUpdateCandidate? Find(
        PluginHubTarget target,
        PluginHubEntry entry,
        PackageVersion hostVersion,
        int hostApiVersion)
    {
        if (!string.Equals(entry.Id, target.PluginId, StringComparison.Ordinal)
            || entry.ApiVersion != hostApiVersion
            || (entry.MinHostVersion is { } minimum && hostVersion < minimum))
        {
            return null;
        }

        var release = entry.SelectLatestRelease();

        if (release is null
            || release.Version <= target.Version
            || !PluginHubEndpoint.TryResolvePackage(target.Hub, release.Url, out var package))
        {
            return null;
        }

        return new PluginHubUpdateCandidate(
            target.PluginId,
            target.Hub,
            release.Version,
            package,
            release.Sha256,
            release.Size);
    }
}
