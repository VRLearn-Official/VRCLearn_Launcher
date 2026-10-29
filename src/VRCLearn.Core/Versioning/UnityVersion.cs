using System.Globalization;

namespace VRCLearn.Core.Versioning;

/// <summary>比较时忽略中国版后缀（c1），但相等性把它计入。</summary>
public readonly struct UnityVersion : IComparable<UnityVersion>, IEquatable<UnityVersion>
{
    private static readonly char[] ReleaseTypeSymbols = ['a', 'b', 'f', 'c', 'p', 'x'];

    public UnityVersion(ushort major, byte minor, byte revision, ReleaseType type, byte increment)
        : this(major, minor, revision, type, increment, null)
    {
    }

    public UnityVersion(
        ushort major,
        byte minor,
        byte revision,
        ReleaseType type,
        byte increment,
        byte? chinaIncrement)
    {
        Major = major;
        Minor = minor;
        Revision = revision;
        Type = type;
        Increment = increment;
        ChinaIncrement = chinaIncrement;
    }

    public ushort Major { get; }

    public byte Minor { get; }

    public byte Revision { get; }

    public ReleaseType Type { get; }

    public byte Increment { get; }

    public byte? ChinaIncrement { get; }

    public static UnityVersion? TryParse(string? input)
    {
        if (input is null)
        {
            return null;
        }

        var firstDot = input.IndexOf('.');
        if (firstDot < 0 || !TryParseNumber(input[..firstDot], out ushort major))
        {
            return null;
        }

        var afterMajor = input[(firstDot + 1)..];
        var secondDot = afterMajor.IndexOf('.');
        if (secondDot < 0 || !TryParseNumber(afterMajor[..secondDot], out byte minor))
        {
            return null;
        }

        var rest = afterMajor[(secondDot + 1)..];
        var delimiter = rest.IndexOfAny(ReleaseTypeSymbols);
        if (delimiter < 0
            || !TryParseNumber(rest[..delimiter], out byte revision)
            || !ReleaseType.TryParse(rest[delimiter], out var type))
        {
            return null;
        }

        var tail = rest[(delimiter + 1)..];
        var hyphen = tail.IndexOf('-');
        var incrementText = hyphen < 0 ? tail : tail[..hyphen];

        var chinaMarker = incrementText.IndexOf('c');
        if (chinaMarker < 0)
        {
            return TryParseNumber(incrementText, out byte increment)
                ? new UnityVersion(major, minor, revision, type, increment)
                : null;
        }

        if (!TryParseNumber(incrementText[..chinaMarker], out byte baseIncrement)
            || !TryParseNumber(incrementText[(chinaMarker + 1)..], out byte chinaIncrement)
            || chinaIncrement == 0)
        {
            return null;
        }

        return new UnityVersion(major, minor, revision, type, baseIncrement, chinaIncrement);
    }

    public int CompareTo(UnityVersion other)
    {
        var ordering = Major.CompareTo(other.Major);
        if (ordering != 0)
        {
            return ordering;
        }

        ordering = Minor.CompareTo(other.Minor);
        if (ordering != 0)
        {
            return ordering;
        }

        ordering = Revision.CompareTo(other.Revision);
        if (ordering != 0)
        {
            return ordering;
        }

        ordering = Type.CompareTo(other.Type);
        return ordering != 0 ? ordering : Increment.CompareTo(other.Increment);
    }

    public bool Equals(UnityVersion other) =>
        Major == other.Major
        && Minor == other.Minor
        && Revision == other.Revision
        && Type.Equals(other.Type)
        && Increment == other.Increment
        && ChinaIncrement == other.ChinaIncrement;

    public override bool Equals(object? obj) => obj is UnityVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Revision, Type, Increment, ChinaIncrement);

    public override string ToString() =>
        ChinaIncrement is { } china
            ? $"{Major}.{Minor}.{Revision}{Type.Symbol}{Increment}c{china}"
            : $"{Major}.{Minor}.{Revision}{Type.Symbol}{Increment}";

    public static bool operator ==(UnityVersion left, UnityVersion right) => left.Equals(right);

    public static bool operator !=(UnityVersion left, UnityVersion right) => !left.Equals(right);

    public static bool operator <(UnityVersion left, UnityVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(UnityVersion left, UnityVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(UnityVersion left, UnityVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(UnityVersion left, UnityVersion right) => left.CompareTo(right) >= 0;

    private static bool TryParseNumber(string text, out ushort value) =>
        ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryParseNumber(string text, out byte value) =>
        byte.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
