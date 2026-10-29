namespace VRCLearn.Core.Versioning;

/// <summary>排序为 a &lt; b &lt; f = c &lt; p &lt; x（f 与 c 等价）。</summary>
public readonly struct ReleaseType : IEquatable<ReleaseType>, IComparable<ReleaseType>
{
    private readonly byte _kind;

    private ReleaseType(byte kind) => _kind = kind;

    public static ReleaseType Alpha => new(0);

    public static ReleaseType Beta => new(1);

    public static ReleaseType Normal => new(2);

    public static ReleaseType China => new(3);

    public static ReleaseType Patch => new(4);

    public static ReleaseType Experimental => new(5);

    private int Rank => _kind switch
    {
        0 => 0,
        1 => 1,
        2 or 3 => 2,
        4 => 3,
        _ => 4,
    };

    public char Symbol => _kind switch
    {
        0 => 'a',
        1 => 'b',
        2 => 'f',
        3 => 'c',
        4 => 'p',
        _ => 'x',
    };

    public static bool TryParse(char symbol, out ReleaseType type)
    {
        switch (symbol)
        {
            case 'a':
                type = Alpha;
                return true;
            case 'b':
                type = Beta;
                return true;
            case 'f':
                type = Normal;
                return true;
            case 'c':
                type = China;
                return true;
            case 'p':
                type = Patch;
                return true;
            case 'x':
                type = Experimental;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public int CompareTo(ReleaseType other) => Rank.CompareTo(other.Rank);

    public bool Equals(ReleaseType other) => Rank == other.Rank;

    public override bool Equals(object? obj) => obj is ReleaseType other && Equals(other);

    public override int GetHashCode() => Rank;

    public override string ToString() => Symbol.ToString();

    public static bool operator ==(ReleaseType left, ReleaseType right) => left.Equals(right);

    public static bool operator !=(ReleaseType left, ReleaseType right) => !left.Equals(right);

    public static bool operator <(ReleaseType left, ReleaseType right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseType left, ReleaseType right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseType left, ReleaseType right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseType left, ReleaseType right) => left.CompareTo(right) >= 0;
}
