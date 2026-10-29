namespace VRCLearn.Core.Versioning;

public readonly struct Prerelease : IComparable<Prerelease>, IEquatable<Prerelease>
{
    private readonly string? _text;

    private Prerelease(string? text) => _text = text;

    public static Prerelease Empty => default;

    public bool IsEmpty => string.IsNullOrEmpty(_text);

    public string AsString => _text ?? "";

    public static Prerelease Parse(string text)
    {
        var buffer = new ParsingBuffer(text);
        var prerelease = Read(buffer);
        if (!buffer.IsEmpty)
        {
            throw new VersionParseException(VersionParseError.Invalid);
        }

        return prerelease;
    }

    internal static Prerelease Read(ParsingBuffer buffer) =>
        new(IdentifierParser.Parse(buffer, allowLeadingZero: false));

    public int CompareTo(Prerelease other)
    {
        if (IsEmpty)
        {
            return other.IsEmpty ? 0 : 1;
        }

        if (other.IsEmpty)
        {
            return -1;
        }

        var left = AsString.Split('.');
        var right = other.AsString.Split('.');
        var shared = Math.Min(left.Length, right.Length);

        for (var index = 0; index < shared; index++)
        {
            var ordering = CompareIdentifier(left[index], right[index]);
            if (ordering != 0)
            {
                return ordering;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);

        if (leftNumeric && rightNumeric)
        {
            var byLength = left.Length.CompareTo(right.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
        }

        if (leftNumeric)
        {
            return -1;
        }

        return rightNumeric ? 1 : string.CompareOrdinal(left, right);
    }

    public bool Equals(Prerelease other) =>
        string.Equals(AsString, other.AsString, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Prerelease other && Equals(other);

    public override int GetHashCode() => AsString.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => AsString;

    public static bool operator ==(Prerelease left, Prerelease right) => left.Equals(right);

    public static bool operator !=(Prerelease left, Prerelease right) => !left.Equals(right);

    public static bool operator <(Prerelease left, Prerelease right) => left.CompareTo(right) < 0;

    public static bool operator >(Prerelease left, Prerelease right) => left.CompareTo(right) > 0;

    public static bool operator <=(Prerelease left, Prerelease right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Prerelease left, Prerelease right) => left.CompareTo(right) >= 0;
}
