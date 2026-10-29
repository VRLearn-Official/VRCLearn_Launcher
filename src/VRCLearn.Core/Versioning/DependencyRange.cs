namespace VRCLearn.Core.Versioning;

public sealed class DependencyRange : IEquatable<DependencyRange>
{
    private readonly VersionRange _range;

    private DependencyRange(VersionRange range) => _range = range;

    public static DependencyRange FromVersion(PackageVersion version) =>
        new(VersionRange.Star(version));

    public static DependencyRange Parse(string text) => new(VersionRange.Parse(text));

    public PackageVersion? AsSingleVersion()
    {
        if (_range.ComparatorSets is not [var set])
        {
            return null;
        }

        if (set.Comparators is not [var comparator] || comparator.Kind != ComparatorKind.Star)
        {
            return null;
        }

        return comparator.First.ToFull();
    }

    public bool Matches(PackageVersion version) =>
        AsSingleVersion() is { } single
            ? single.CompareTo(version) <= 0
            : _range.Matches(version, PrereleaseAcceptance.Allow);

    public DependencyRange Intersect(DependencyRange other) =>
        new(AsRange().Intersect(other.AsRange()));

    public VersionRange AsRange() =>
        AsSingleVersion() is { } single ? VersionRange.SameOrLater(single) : _range;

    public bool Equals(DependencyRange? other) => other is not null && _range.Equals(other._range);

    public override bool Equals(object? obj) => Equals(obj as DependencyRange);

    public override int GetHashCode() => _range.GetHashCode();

    public override string ToString() => _range.ToString();
}
