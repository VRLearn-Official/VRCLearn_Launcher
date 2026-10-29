namespace VRCLearn.Core.Versioning;

internal sealed class ComparatorSet(IReadOnlyList<Comparator> comparators) : IEquatable<ComparatorSet>
{
    public IReadOnlyList<Comparator> Comparators { get; } = comparators;

    public bool ContainsPre => Comparators.Any(x => x.ContainsPre);

    public static ComparatorSet Parse(ParsingBuffer buffer)
    {
        var comparators = new List<Comparator>();

        while (!buffer.IsEmpty)
        {
            comparators.Add(Comparator.Parse(buffer));
            buffer.SkipWhitespace();
        }

        return new ComparatorSet(comparators);
    }

    public bool Matches(PackageVersion version, PrereleaseAcceptance acceptance) =>
        Comparators.All(x => x.Matches(version, acceptance));

    public bool Equals(ComparatorSet? other) =>
        other is not null && Comparators.SequenceEqual(other.Comparators);

    public override bool Equals(object? obj) => Equals(obj as ComparatorSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var comparator in Comparators)
        {
            hash.Add(comparator);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(" ", Comparators);
}
