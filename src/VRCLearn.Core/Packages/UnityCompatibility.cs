using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Packages;

public static class UnityCompatibility
{
    public static bool IsCompatible(PackageManifest package, UnityVersion unity) => package.Name switch
    {
        "com.vrchat.avatars" or "com.vrchat.worlds" or "com.vrchat.base"
            when IsVrcSdkFor2019(package.Version) => unity.Major == 2019,

        "com.vrchat.core.vpm-resolver"
            when IsResolverFor2019(package.Version) => unity.Major == 2019,

        "com.vrchat.avatars" or "com.vrchat.worlds" or "com.vrchat.base"
            when package.Unity is { } target => target.Major == unity.Major && target.Minor == unity.Minor,

        _ => package.Unity is not { } minimum
            || unity >= new UnityVersion(minimum.Major, minimum.Minor, 0, ReleaseType.Alpha, 0),
    };

    private static bool IsVrcSdkFor2019(PackageVersion version) =>
        version.Major == 3 && version.Minor <= 4;

    private static bool IsResolverFor2019(PackageVersion version) =>
        version.Major == 0 && version.Minor == 1 && version.Patch <= 26;
}
