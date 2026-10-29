namespace VRCLearn.Core.Versioning;

public readonly struct BuildMetadata : IEquatable<BuildMetadata>
{
    private readonly string? _text;

    private BuildMetadata(string? text) => _text = text;

    public static BuildMetadata Empty => default;

    public bool IsEmpty => string.IsNullOrEmpty(_text);

    public string AsString => _text ?? "";

    public static BuildMetadata Parse(string text)
    {
        var buffer = new ParsingBuffer(text);
        var build = Read(buffer);
        if (!buffer.IsEmpty)
        {
            throw new VersionParseException(VersionParseError.Invalid);
        }

        return build;
    }

    internal static BuildMetadata Read(ParsingBuffer buffer) =>
        new(IdentifierParser.Parse(buffer, allowLeadingZero: true));

    public bool Equals(BuildMetadata other) =>
        string.Equals(AsString, other.AsString, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is BuildMetadata other && Equals(other);

    public override int GetHashCode() => AsString.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => AsString;

    public static bool operator ==(BuildMetadata left, BuildMetadata right) => left.Equals(right);

    public static bool operator !=(BuildMetadata left, BuildMetadata right) => !left.Equals(right);
}
