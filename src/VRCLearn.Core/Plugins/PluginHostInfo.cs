using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins;

public static class PluginHostInfo
{
    public static PackageVersion Version { get; } = ReadVersion();

    public const int ApiVersion = PluginApiVersions.Current;

    private static PackageVersion ReadVersion()
    {
        var version = typeof(PluginHostInfo).Assembly.GetName().Version;

        return version is null
            ? new PackageVersion(0, 0, 0)
            : new PackageVersion(version.Major, version.Minor, Math.Max(version.Build, 0));
    }
}
