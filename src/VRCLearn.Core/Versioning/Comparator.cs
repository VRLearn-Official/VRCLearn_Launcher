namespace VRCLearn.Core.Versioning;

internal enum ComparatorKind
{
    Tilde,
    Caret,
    Exact,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Hyphen,
    Star,
}

internal sealed record Comparator(ComparatorKind Kind, PartialVersion First, PartialVersion? Second)
{
    public static Comparator Parse(ParsingBuffer buffer)
    {
        buffer.SkipWhitespace();

        switch (buffer.First)
        {
            case '~':
                buffer.Skip();
                return new Comparator(ComparatorKind.Tilde, PartialVersion.Parse(buffer), null);

            case '^':
                buffer.Skip();
                return new Comparator(ComparatorKind.Caret, PartialVersion.Parse(buffer), null);

            case '=':
                buffer.Skip();
                return new Comparator(ComparatorKind.Exact, PartialVersion.Parse(buffer), null);

            case '>':
                buffer.Skip();
                if (buffer.First == '=')
                {
                    buffer.Skip();
                    buffer.SkipWhitespace();
                    return new Comparator(ComparatorKind.GreaterThanOrEqual, PartialVersion.Parse(buffer), null);
                }

                buffer.SkipWhitespace();
                return new Comparator(ComparatorKind.GreaterThan, PartialVersion.Parse(buffer), null);

            case '<':
                buffer.Skip();
                if (buffer.First == '=')
                {
                    buffer.Skip();
                    buffer.SkipWhitespace();
                    return new Comparator(ComparatorKind.LessThanOrEqual, PartialVersion.Parse(buffer), null);
                }

                buffer.SkipWhitespace();
                return new Comparator(ComparatorKind.LessThan, PartialVersion.Parse(buffer), null);

            case null:
                throw new VersionParseException(VersionParseError.UnexpectedEnd);

            default:
                var lower = PartialVersion.Parse(buffer);
                buffer.SkipWhitespace();
                if (buffer.First == '-')
                {
                    buffer.SkipWhitespace();
                    buffer.Skip();
                    var upper = PartialVersion.Parse(buffer);
                    return new Comparator(ComparatorKind.Hyphen, lower, upper);
                }

                return new Comparator(ComparatorKind.Star, lower, null);
        }
    }

    public bool ContainsPre => Kind == ComparatorKind.Hyphen
        ? !First.Pre.IsEmpty || Second is { Pre.IsEmpty: false }
        : !First.Pre.IsEmpty;

    public bool Matches(PackageVersion version, PrereleaseAcceptance acceptance)
    {
        if (!MatchesInternal(version))
        {
            return false;
        }

        if (version.IsStable)
        {
            return true;
        }

        return acceptance switch
        {
            PrereleaseAcceptance.Deny => false,
            PrereleaseAcceptance.Allow => true,
            _ => MatchesPreMinimum(),
        };
    }

    public override string ToString() => Kind switch
    {
        ComparatorKind.Tilde => $"~{First}",
        ComparatorKind.Caret => $"^{First}",
        ComparatorKind.Exact => $"={First}",
        ComparatorKind.GreaterThan => $">{First}",
        ComparatorKind.GreaterThanOrEqual => $">={First}",
        ComparatorKind.LessThan => $"<{First}",
        ComparatorKind.LessThanOrEqual => $"<={First}",
        ComparatorKind.Hyphen => $"{First} - {Second}",
        _ => First.ToString(),
    };

    private bool MatchesPreMinimum() =>
        First.ToZeros().IsPre || (Second is not null && Second.ToZeros().IsPre);

    private bool MatchesInternal(PackageVersion version) => Kind switch
    {
        ComparatorKind.Tilde => MatchesTilde(version),
        ComparatorKind.Caret => MatchesCaret(version),
        ComparatorKind.Star or ComparatorKind.Exact => MatchesStarOrExact(version),
        ComparatorKind.GreaterThan => GreaterThan(version, First),
        ComparatorKind.GreaterThanOrEqual => GreaterThanOrEqual(version, First),
        ComparatorKind.LessThan => LessThan(version, First),
        ComparatorKind.LessThanOrEqual => LessThanOrEqual(version, First),
        _ => GreaterThanOrEqual(version, First) && LessThanOrEqual(version, Second!),
    };

    private bool MatchesTilde(PackageVersion version)
    {
        if (version < First.ToZeros())
        {
            return false;
        }

        if (version.Major != First.MajorOr(0))
        {
            return false;
        }

        return First.Minor.AsNumber() is not { } minor || version.Minor == minor;
    }

    private bool MatchesCaret(PackageVersion version)
    {
        if (version < First.ToZeros())
        {
            return false;
        }

        if (First.Major.AsNumber() is null)
        {
            return true;
        }

        if (version.Major != First.MajorOr(0))
        {
            return false;
        }

        if (First.MajorOr(0) == 0
            && First.Minor.AsNumber() is { } minor)
        {
            if (version.Minor != minor)
            {
                return false;
            }

            if (First.Patch.AsNumber() is { } patch && minor == 0 && version.Patch != patch)
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesStarOrExact(PackageVersion version)
    {
        var (bound, isFull) = First.ToFullOrNext();

        return isFull
            ? bound.CompareTo(version) == 0
            : First.ToZerosWithPrerelease() <= version && version < bound;
    }

    private static bool GreaterThan(PackageVersion version, PartialVersion partial)
    {
        var (bound, isFull) = partial.ToFullOrNext();
        return isFull ? version > bound : version >= bound;
    }

    private static bool GreaterThanOrEqual(PackageVersion version, PartialVersion partial)
    {
        var full = partial.ToFull();
        return full is not null ? version >= full : version >= partial.ToZerosWithPrerelease();
    }

    private static bool LessThan(PackageVersion version, PartialVersion partial)
    {
        var full = partial.ToFull();
        return full is not null ? version < full : version < partial.ToZerosWithPrerelease();
    }

    private static bool LessThanOrEqual(PackageVersion version, PartialVersion partial)
    {
        var (bound, isFull) = partial.ToFullOrNext();
        return isFull ? version <= bound : version < bound;
    }
}
