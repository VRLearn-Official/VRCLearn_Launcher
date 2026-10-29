namespace VRCLearn.Core.Versioning;

/// <summary>范围表达式里的一段版本，允许缺省（x / X / * 或未写出的段）。</summary>
internal sealed record PartialVersion(
    Segment Major,
    Segment Minor,
    Segment Patch,
    Prerelease Pre,
    BuildMetadata Build)
{
    private static readonly Prerelease ZeroPrerelease = Prerelease.Parse("0");

    public static PartialVersion FromVersion(PackageVersion version) => new(
        Segment.FromNumber(version.Major),
        Segment.FromNumber(version.Minor),
        Segment.FromNumber(version.Patch),
        version.Pre,
        version.Build);

    public static PartialVersion Parse(ParsingBuffer buffer)
    {
        buffer.SkipWhitespace();

        if (buffer.First == 'v')
        {
            buffer.Skip();
        }

        var major = ParseSegment(buffer);

        var minor = Segment.Missing;
        if (buffer.First == '.')
        {
            buffer.Skip();
            minor = ParseSegment(buffer);
        }

        var patch = Segment.Missing;
        var prerelease = Prerelease.Empty;
        var build = BuildMetadata.Empty;

        if (buffer.First == '.')
        {
            buffer.Skip();
            patch = ParseSegment(buffer);

            if (buffer.First == '-')
            {
                if (buffer.At(1) is null or '.')
                {
                    prerelease = Prerelease.Parse(IdentifierParser.Parse(buffer, allowLeadingZero: false));
                }
                else
                {
                    buffer.Skip();
                    prerelease = Prerelease.Parse(IdentifierParser.Parse(buffer, allowLeadingZero: false));
                }
            }
            else if (buffer.First is { } next && IsIdentifierStart(next))
            {
                prerelease = Prerelease.Parse(IdentifierParser.Parse(buffer, allowLeadingZero: false));
            }

            if (buffer.First == '+')
            {
                buffer.Skip();
                build = BuildMetadata.Parse(IdentifierParser.Parse(buffer, allowLeadingZero: false));
            }
        }

        return new PartialVersion(major, minor, patch, prerelease, build);
    }

    public PackageVersion? ToFull()
    {
        if (Major.AsNumber() is { } major && Minor.AsNumber() is { } minor && Patch.AsNumber() is { } patch)
        {
            return new PackageVersion(major, minor, patch, Pre, Build);
        }

        return null;
    }

    public (PackageVersion Bound, bool IsFull) ToFullOrNext()
    {
        if (Major.AsNumber() is not { } major)
        {
            return (new PackageVersion(long.MaxValue, long.MaxValue, long.MaxValue, ZeroPrerelease), false);
        }

        if (Minor.AsNumber() is not { } minor)
        {
            return (new PackageVersion(major + 1, 0, 0, ZeroPrerelease), false);
        }

        if (Patch.AsNumber() is not { } patch)
        {
            return (new PackageVersion(major, minor + 1, 0, ZeroPrerelease), false);
        }

        return (new PackageVersion(major, minor, patch, Pre, Build), true);
    }

    public PackageVersion ToZeros() =>
        new(MajorOr(0), MinorOr(0), PatchOr(0), Pre, Build);

    public PackageVersion ToZerosWithPrerelease() =>
        new(MajorOr(0), MinorOr(0), PatchOr(0), Pre.IsEmpty ? ZeroPrerelease : Pre, Build);

    public long MajorOr(long fallback) => Major.AsNumber() ?? fallback;

    public long MinorOr(long fallback) => Minor.AsNumber() ?? fallback;

    public long PatchOr(long fallback) => Patch.AsNumber() ?? fallback;

    public override string ToString()
    {
        var text = WriteSegment(Major, "")
            + WriteSegment(Minor, ".")
            + WriteSegment(Patch, ".");

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

    private static string WriteSegment(Segment segment, string prefix) =>
        segment.Value == Segment.NotExists ? "" : prefix + segment.Text;

    private static Segment ParseSegment(ParsingBuffer buffer)
    {
        switch (buffer.First)
        {
            case 'x':
            case 'X':
            case '*':
                var text = buffer.Take(1);
                return Segment.TryParse(text, out var wildcard)
                    ? wildcard
                    : throw new VersionParseException(VersionParseError.Invalid);

            case '0':
                buffer.Skip();
                if (buffer.First is >= '0' and <= '9')
                {
                    throw new VersionParseException(VersionParseError.Invalid);
                }

                return Segment.Zero;

            case >= '1' and <= '9':
                var length = 1;
                while (buffer.At(length) is >= '0' and <= '9')
                {
                    length++;
                }

                return Segment.TryParse(buffer.Take(length), out var number)
                    ? number
                    : throw new VersionParseException(VersionParseError.TooBig);

            default:
                throw new VersionParseException(VersionParseError.Invalid);
        }
    }

    private static bool IsIdentifierStart(char value) =>
        value is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-';

    public bool Equals(PartialVersion? other) =>
        other is not null
        && Major == other.Major
        && Minor == other.Minor
        && Patch == other.Patch
        && Pre.Equals(other.Pre)
        && Build.Equals(other.Build);

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch, Pre, Build);
}
