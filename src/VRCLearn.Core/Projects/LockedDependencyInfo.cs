using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

/// <summary><c>vpm-manifest.json</c> 里 locked 段的一条记录。</summary>
public sealed record LockedDependencyInfo(
    string Name,
    PackageVersion Version,
    IReadOnlyDictionary<string, VersionRange> Dependencies)
{
    private static readonly IReadOnlyDictionary<string, VersionRange> None =
        new Dictionary<string, VersionRange>();

    public static LockedDependencyInfo Create(
        string name,
        PackageVersion version,
        IReadOnlyDictionary<string, VersionRange>? dependencies) =>
        new(name, version, dependencies ?? None);
}
