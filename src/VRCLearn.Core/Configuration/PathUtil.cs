namespace VRCLearn.Core.Configuration;

/// <summary>一律存绝对路径，不带结尾分隔符（盘符根除外）。比较不区分大小写。</summary>
public static class PathUtil
{
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool SamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static string NameOf(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    public static string? ParentOf(string path) =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));

    public static bool IsUnder(string path, string ancestor)
    {
        var root = Normalize(ancestor);

        return Normalize(path).StartsWith(
            root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>只由 ASCII 字母数字与 <c>. _ -</c> 组成，可以直接当文件名用。</summary>
    public static bool IsSafeFileName(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '_' or '-');

    /// <summary>CON、NUL、COM1 之类的设备名，带扩展名也算。</summary>
    public static bool IsReservedName(string name) => ReservedNames.Contains(name.Split('.')[0]);

    public static bool IsSafePathSegment(string? segment, bool allowColon = false) =>
        !string.IsNullOrEmpty(segment)
        && segment is not "." and not ".."
        && segment.IndexOfAny(allowColon ? InvalidCharsAllowingColon : InvalidChars) < 0
        && segment.TrimEnd('.', ' ') == segment
        && !IsReservedName(segment);

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    private static readonly char[] InvalidCharsAllowingColon =
        [.. InvalidChars.Where(x => x != ':')];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
}
