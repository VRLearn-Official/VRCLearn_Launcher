using System.Globalization;

namespace VRCLearn.Core.Packages;

/// <summary>包清单里 <c>unity</c> 字段的值，形如 <c>2022.3</c> 或 <c>2022</c>。</summary>
public sealed record PartialUnityVersion(ushort Major, byte Minor)
{
    public static PartialUnityVersion? TryParse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var dot = text.IndexOf('.');
        if (dot < 0)
        {
            return TryParseUInt16(text, out var onlyMajor) ? new PartialUnityVersion(onlyMajor, 0) : null;
        }

        if (!TryParseUInt16(text[..dot], out var major) || !TryParseByte(text[(dot + 1)..], out var minor))
        {
            return null;
        }

        return new PartialUnityVersion(major, minor);
    }

    public override string ToString() => Minor == 0 ? Major.ToString(CultureInfo.InvariantCulture) : $"{Major}.{Minor}";

    private static bool TryParseUInt16(string text, out ushort value) =>
        ushort.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryParseByte(string text, out byte value) =>
        byte.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
