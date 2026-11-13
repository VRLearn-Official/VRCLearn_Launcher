using VRCLearn.Core.Archives;

namespace VRCLearn.Core.Projects;

public static class UnityProjectRootLocator
{
    private const string SettingsFolder = "ProjectSettings";
    private const string VersionFile = "ProjectVersion.txt";

    public static string Locate(IEnumerable<string> entryNames)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in entryNames)
        {
            if (IsDirectoryEntry(raw) || IsMacMetadata(raw))
            {
                continue;
            }

            var normalized = ZipEntryPath.Normalize(raw)
                ?? throw new InvalidDataException($"directory traversal detected: {raw}");

            if (RootOf(normalized) is { } root)
            {
                candidates.Add(root);
            }
        }

        if (candidates.Count == 0)
        {
            throw new UnityProjectNotFoundException();
        }

        var depth = candidates.Min(Depth);
        var shallowest = candidates
            .Where(x => Depth(x) == depth)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return shallowest.Length == 1
            ? shallowest[0]
            : throw new AmbiguousUnityProjectRootException(shallowest);
    }

    public static string DisplayName(string prefix) =>
        prefix.Length == 0 ? "（压缩包根目录）" : prefix;

    private static string? RootOf(string normalized)
    {
        var segments = normalized.Split('/');

        if (segments.Length < 2
            || !segments[^2].Equals(SettingsFolder, StringComparison.OrdinalIgnoreCase)
            || !segments[^1].Equals(VersionFile, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Join('/', segments[..^2]);
    }

    private static int Depth(string prefix) =>
        prefix.Length == 0 ? 0 : prefix.Count(x => x == '/') + 1;

    private static bool IsDirectoryEntry(string name) =>
        name.EndsWith('/') || name.EndsWith('\\');

    private static bool IsMacMetadata(string name)
    {
        var normalized = name.Replace('\\', '/');

        return normalized.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
            || normalized.Split('/').Any(x => x.StartsWith("._", StringComparison.Ordinal));
    }
}

public class UnityProjectRootException : Exception
{
    protected UnityProjectRootException(string message) : base(message)
    {
    }
}

public sealed class UnityProjectNotFoundException : UnityProjectRootException
{
    public UnityProjectNotFoundException()
        : base("备份里没有找到 ProjectSettings/ProjectVersion.txt，无法确认这是 Unity 项目，已取消还原。")
    {
    }
}

public sealed class AmbiguousUnityProjectRootException : UnityProjectRootException
{
    public AmbiguousUnityProjectRootException(IReadOnlyList<string> candidates)
        : base(
            $"备份里有多个 Unity 项目根目录，无法确定要还原哪一个：{string.Join('、', candidates.Select(UnityProjectRootLocator.DisplayName))}。请整理压缩包后重试。")
    {
    }
}
