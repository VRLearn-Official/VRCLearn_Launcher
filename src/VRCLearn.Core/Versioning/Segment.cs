using System.Globalization;

namespace VRCLearn.Core.Versioning;

/// <summary>除普通数字外，还承载范围表达式里的通配符与「不存在」哨兵值。</summary>
internal readonly struct Segment : IEquatable<Segment>
{
    public const long NotExists = long.MaxValue;
    public const long Star = long.MaxValue - 1;
    public const long UpperX = long.MaxValue - 2;
    public const long LowerX = long.MaxValue - 3;

    /// <summary>可作为普通数字使用的上界。</summary>
    public const long Max = long.MaxValue / 2;

    public static readonly Segment Zero = new(0);
    public static readonly Segment Missing = new(NotExists);

    private readonly long _value;

    private Segment(long value) => _value = value;

    public long Value => _value;

    public long? AsNumber() => _value <= Max ? _value : null;

    public static Segment FromNumber(long value) =>
        value <= Max ? new Segment(value) : throw new ArgumentOutOfRangeException(nameof(value));

    public static bool TryParse(string text, out Segment segment)
    {
        switch (text)
        {
            case "x":
                segment = new Segment(LowerX);
                return true;
            case "X":
                segment = new Segment(UpperX);
                return true;
            case "*":
                segment = new Segment(Star);
                return true;
        }

        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value <= Max)
        {
            segment = new Segment(value);
            return true;
        }

        segment = Missing;
        return false;
    }

    public string Text => _value switch
    {
        Star => "*",
        UpperX => "X",
        LowerX => "x",
        _ => _value.ToString(CultureInfo.InvariantCulture),
    };

    public bool Equals(Segment other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Segment other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public static bool operator ==(Segment left, Segment right) => left.Equals(right);

    public static bool operator !=(Segment left, Segment right) => !left.Equals(right);
}
