using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace VRCLearn.Core.Versioning;

/// <summary>比较与相等性忽略 build metadata，需要连 build 比较时用 <see cref="StrictEquals"/>。</summary>
public sealed class PackageVersion : IComparable<PackageVersion>, IEquatable<PackageVersion>
{
    public PackageVersion(long major, long minor, long patch)
        : this(major, minor, patch, Prerelease.Empty, BuildMetadata.Empty)
    {
    }

    public PackageVersion(long major, long minor, long patch, Prerelease prerelease)
        : this(major, minor, patch, prerelease, BuildMetadata.Empty)
    {
    }

    public PackageVersion(long major, long minor, long patch, Prerelease prerelease, BuildMetadata build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Pre = prerelease;
        Build = build;
    }

    public long Major { get; }

    public long Minor { get; }

    public long Patch { get; }

    public Prerelease Pre { get; }

    public BuildMetadata Build { get; }

    public bool IsStable => Pre.IsEmpty;

    public bool IsPre => !IsStable;

    public PackageVersion BaseVersion => new(Major, Minor, Patch);

    public static PackageVersion Parse(string text)
    {
        var buffer = new ParsingBuffer(text);
        var version = Read(buffer);
        if (!buffer.IsEmpty)
        {
            throw new VersionParseException(VersionParseError.Invalid);
        }

        return version;
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out PackageVersion? version)
    {
        if (text is null)
        {
            version = null;
            return false;
        }

        try
        {
            version = Parse(text);
            return true;
        }
        catch (VersionParseException)
        {
            version = null;
            return false;
        }
    }

    internal static PackageVersion Read(ParsingBuffer buffer)
    {
        var major = ReadSegment(buffer);
        buffer.Expect('.');
        var minor = ReadSegment(buffer);
        buffer.Expect('.');
        var patch = ReadSegment(buffer);

        var prerelease = Prerelease.Empty;
        if (buffer.First == '-')
        {
            buffer.Skip();
            prerelease = Prerelease.Read(buffer);
        }

        var build = BuildMetadata.Empty;
        if (buffer.First == '+')
        {
            buffer.Skip();
            build = BuildMetadata.Read(buffer);
        }

        return new PackageVersion(major, minor, patch, prerelease, build);
    }

    public int CompareTo(PackageVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

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

        ordering = Patch.CompareTo(other.Patch);
        return ordering != 0 ? ordering : Pre.CompareTo(other.Pre);
    }

    public bool StrictEquals(PackageVersion? other) =>
        other is not null && Equals(other) && Build.Equals(other.Build);

    public bool Equals(PackageVersion? other) =>
        other is not null
        && Major == other.Major
        && Minor == other.Minor
        && Patch == other.Patch
        && Pre.Equals(other.Pre);

    public override bool Equals(object? obj) => Equals(obj as PackageVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Pre);

    public override string ToString()
    {
        var text = $"{Major}.{Minor}.{Patch}";

        if (!Pre.IsEmpty)
        {
            text += $"-{Pre}";
        }

        if (!Build.IsEmpty)
        {
            text += $"+{Build}";
        }

        return text;
    }

    public static bool operator ==(PackageVersion? left, PackageVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(PackageVersion? left, PackageVersion? right) => !(left == right);

    public static bool operator <(PackageVersion? left, PackageVersion? right) =>
        Comparer<PackageVersion?>.Default.Compare(left, right) < 0;

    public static bool operator >(PackageVersion? left, PackageVersion? right) =>
        Comparer<PackageVersion?>.Default.Compare(left, right) > 0;

    public static bool operator <=(PackageVersion? left, PackageVersion? right) =>
        Comparer<PackageVersion?>.Default.Compare(left, right) <= 0;

    public static bool operator >=(PackageVersion? left, PackageVersion? right) =>
        Comparer<PackageVersion?>.Default.Compare(left, right) >= 0;

    private static long ReadSegment(ParsingBuffer buffer)
    {
        switch (buffer.First)
        {
            case '0':
                buffer.Skip();
                if (buffer.First is >= '0' and <= '9')
                {
                    throw new VersionParseException(VersionParseError.Invalid);
                }

                return 0;

            case >= '1' and <= '9':
                var length = 1;
                while (buffer.At(length) is >= '0' and <= '9')
                {
                    length++;
                }

                return ReadNumber(buffer.Take(length));

            default:
                throw new VersionParseException(VersionParseError.Invalid);
        }
    }

    private static long ReadNumber(string digits)
    {
        if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value <= Segment.Max)
        {
            return value;
        }

        throw new VersionParseException(VersionParseError.TooBig);
    }
}
