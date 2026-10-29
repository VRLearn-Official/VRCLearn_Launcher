using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Unity;

public static class UnityReleaseCatalog
{
    internal const string EditorHash = "887be4894c44";

    private static readonly UnityVersion Stable = new(2022, 3, 22, ReleaseType.Normal, 1);

    public static IReadOnlyList<UnityVersion> SupportedVersions { get; } =
    [
        Stable,
        new(2022, 3, 6, ReleaseType.Normal, 1),
        new(2019, 4, 31, ReleaseType.Normal, 1),
    ];

    public static UnityVersion StableVersion => Stable;
}
