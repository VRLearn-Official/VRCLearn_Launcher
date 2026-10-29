namespace VRCLearn.Core.Versioning;

public enum VersionParseError
{
    Invalid,
    TooBig,
    UnexpectedEnd,
}

public sealed class VersionParseException(VersionParseError error)
    : Exception(Describe(error))
{
    public VersionParseError Error { get; } = error;

    private static string Describe(VersionParseError error) => error switch
    {
        VersionParseError.TooBig => "version segment too big",
        VersionParseError.UnexpectedEnd => "unexpected end of version",
        _ => "invalid version or version range",
    };
}

internal sealed class ParsingBuffer(string source)
{
    private readonly string _source = source;

    public int Position { get; private set; }

    public bool IsEmpty => Position >= _source.Length;

    public char? First => Position < _source.Length ? _source[Position] : null;

    public char? At(int offset)
    {
        var index = Position + offset;
        return index < _source.Length ? _source[index] : null;
    }

    public void Skip() => Position++;

    public void SkipWhitespace()
    {
        while (Position < _source.Length && char.IsWhiteSpace(_source[Position]))
        {
            Position++;
        }
    }

    public void Expect(char expected)
    {
        if (First != expected)
        {
            throw new VersionParseException(VersionParseError.Invalid);
        }

        Position++;
    }

    public string Take(int count)
    {
        var text = _source.Substring(Position, count);
        Position += count;
        return text;
    }

    public string Slice(int start) => _source[start..Position];
}

internal static class IdentifierParser
{
    public static string Parse(ParsingBuffer buffer, bool allowLeadingZero)
    {
        var start = buffer.Position;

        while (true)
        {
            var leadingZero = false;
            var alphanumeric = false;

            switch (buffer.First)
            {
                case null:
                    throw new VersionParseException(VersionParseError.UnexpectedEnd);
                case '0':
                    buffer.Skip();
                    leadingZero = true;
                    break;
                case >= '1' and <= '9':
                    buffer.Skip();
                    break;
                case >= 'a' and <= 'z':
                case >= 'A' and <= 'Z':
                case '-':
                    buffer.Skip();
                    alphanumeric = true;
                    break;
                default:
                    throw new VersionParseException(VersionParseError.Invalid);
            }

            var nextSegment = false;
            while (!nextSegment)
            {
                switch (buffer.First)
                {
                    case >= '0' and <= '9':
                        buffer.Skip();
                        break;
                    case >= 'a' and <= 'z':
                    case >= 'A' and <= 'Z':
                    case '-':
                        buffer.Skip();
                        alphanumeric = true;
                        break;
                    case '.':
                        buffer.Skip();
                        RejectLeadingZero(allowLeadingZero, alphanumeric, leadingZero);
                        nextSegment = true;
                        break;
                    default:
                        RejectLeadingZero(allowLeadingZero, alphanumeric, leadingZero);
                        return buffer.Slice(start);
                }
            }
        }
    }

    private static void RejectLeadingZero(bool allowLeadingZero, bool alphanumeric, bool leadingZero)
    {
        if (!allowLeadingZero && alphanumeric && leadingZero)
        {
            throw new VersionParseException(VersionParseError.Invalid);
        }
    }
}
