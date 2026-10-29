using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins;

public static class PluginCompatibility
{
    public static IReadOnlyList<string> Check(
        PluginManifest manifest,
        PackageVersion hostVersion,
        int hostApiVersion)
    {
        var errors = new List<string>();

        if (manifest.ApiVersion != hostApiVersion)
        {
            errors.Add($"apiVersion {manifest.ApiVersion} is not supported by host {hostApiVersion}");
        }

        if (manifest.MinHostVersion is { } minimum && hostVersion.CompareTo(minimum) < 0)
        {
            errors.Add($"host {hostVersion} is older than required {minimum}");
        }

        return errors;
    }
}
