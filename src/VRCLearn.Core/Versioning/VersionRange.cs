using System.Diagnostics.CodeAnalysis;

namespace VRCLearn.Core.Versioning;

public enum PrereleaseAcceptance
{
    Deny,
    Allow,
    Minimum,
}

public sealed class VersionRange : IEquatable<VersionRange>
{
    private readonly IReadOnlyList<ComparatorSet> _comparators;

    private VersionRange(IReadOnlyList<ComparatorSet> comparators) => _comparators = comparators;

    internal IReadOnlyList<ComparatorSet> ComparatorSets => _comparators;

    public bool ContainsPre => _comparators.Any(x => x.ContainsPre);

    public static VersionRange SameOrLater(PackageVersion version) => FromComparators(
    [
        new ComparatorSet(
        [
            new Comparator(ComparatorKind.GreaterThanOrEqual, PartialVersion.FromVersion(version), null),
        ]),
    ]);

    public static VersionRange Specific(PackageVersion version) => FromComparators(
    [
        new ComparatorSet(
        [
            new Comparator(ComparatorKind.Exact, PartialVersion.FromVersion(version), null),
        ]),
    ]);

    internal static VersionRange Star(PackageVersion version) => FromComparators(
    [
        new ComparatorSet(
        [
            new Comparator(ComparatorKind.Star, PartialVersion.FromVersion(version), null),
        ]),
    ]);

    internal static VersionRange FromComparators(IReadOnlyList<ComparatorSet> comparators) => new(comparators);

    public static VersionRange Parse(string text)
    {
        var comparators = new List<ComparatorSet>();

        foreach (var part in text.Split("||"))
        {
            comparators.Add(ComparatorSet.Parse(new ParsingBuffer(part)));
        }

        return new VersionRange(comparators);
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionRange? range)
    {
        if (text is null)
        {
            range = null;
            return false;
        }

        try
        {
            range = Parse(text);
            return true;
        }
        catch (VersionParseException)
        {
            range = null;
            return false;
        }
    }

    public bool Matches(PackageVersion version) => Matches(version, PrereleaseAcceptance.Minimum);

    public bool Matches(PackageVersion version, PrereleaseAcceptance acceptance) =>
        _comparators.Any(x => x.Matches(version, acceptance));

    public VersionRange Intersect(VersionRange other) => new(
        _comparators
            .SelectMany(
                _ => other._comparators,
                (mine, theirs) => new ComparatorSet([.. mine.Comparators, .. theirs.Comparators]))
            .ToList());

    public bool Equals(VersionRange? other) =>
        other is not null && _comparators.SequenceEqual(other._comparators);

    public override bool Equals(object? obj) => Equals(obj as VersionRange);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var comparators in _comparators)
        {
            hash.Add(comparators);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(" || ", _comparators);
}
