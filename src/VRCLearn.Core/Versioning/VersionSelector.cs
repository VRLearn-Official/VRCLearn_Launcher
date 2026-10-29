using VRCLearn.Core.Packages;

namespace VRCLearn.Core.Versioning;

/// <summary>除版本本身外，还会过滤掉已撤回、以及与项目 Unity 版本不兼容的包。</summary>
public sealed class VersionSelector
{
    private enum Kind
    {
        Specific,
        Latest,
        Ranges,
    }

    private readonly Kind _kind;
    private readonly PackageVersion? _specific;
    private readonly UnityVersion? _projectUnity;
    private readonly bool _includePrerelease;
    private readonly IReadOnlyList<VersionRange> _ranges;
    private readonly PrereleaseAcceptance _acceptance;

    private VersionSelector(
        Kind kind,
        PackageVersion? specific = null,
        UnityVersion? projectUnity = null,
        bool includePrerelease = false,
        IReadOnlyList<VersionRange>? ranges = null,
        PrereleaseAcceptance acceptance = PrereleaseAcceptance.Minimum)
    {
        _kind = kind;
        _specific = specific;
        _projectUnity = projectUnity;
        _includePrerelease = includePrerelease;
        _ranges = ranges ?? [];
        _acceptance = acceptance;
    }

    public static VersionSelector ForSpecificVersion(PackageVersion version) =>
        new(Kind.Specific, specific: version);

    public static VersionSelector LatestFor(UnityVersion? unityVersion, bool includePrerelease) =>
        new(Kind.Latest, projectUnity: unityVersion, includePrerelease: includePrerelease);

    public static VersionSelector RangeFor(UnityVersion? unityVersion, VersionRange range, PrereleaseAcceptance acceptance) =>
        new(Kind.Ranges, projectUnity: unityVersion, ranges: [range], acceptance: acceptance);

    public static VersionSelector RangesFor(
        UnityVersion? unityVersion,
        IReadOnlyList<VersionRange> ranges,
        PrereleaseAcceptance acceptance) =>
        new(Kind.Ranges, projectUnity: unityVersion, ranges: ranges, acceptance: acceptance);

    public PackageVersion? AsSpecificVersion() => _kind == Kind.Specific ? _specific : null;

    public bool Satisfies(PackageManifest package) => _kind switch
    {
        Kind.Specific => _specific == package.Version,
        Kind.Latest when _includePrerelease => UnityAndYank(package),
        Kind.Latest => package.Version.IsStable && UnityAndYank(package),
        _ => _ranges.All(x => x.Matches(package.Version, _acceptance)) && UnityAndYank(package),
    };

    private bool UnityAndYank(PackageManifest package)
    {
        if (package.IsYanked)
        {
            return false;
        }

        return _projectUnity is not { } unity || UnityCompatibility.IsCompatible(package, unity);
    }
}
