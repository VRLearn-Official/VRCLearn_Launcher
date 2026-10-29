using System.Globalization;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Unity;

public sealed record UnityEditor(
    UnityVersion Version,
    string ExecutablePath,
    ChipArchitecture? Architecture)
{
    /// <summary>去重时未知架构按 x64 算。</summary>
    public ChipArchitecture ArchitectureForDedup => Architecture ?? ChipArchitecture.X86_64;

    /// <summary>只有 major 与 minor 都一致才算。</summary>
    public bool MatchesProject(UnityVersion projectVersion) =>
        Version.Major == projectVersion.Major && Version.Minor == projectVersion.Minor;
}

public interface IUnityEditorLocator
{
    /// <summary>找不到返回空列表，不报错。</summary>
    IReadOnlyList<UnityEditor> FindEditors();

    /// <summary>让 Unity Hub headless 报一遍。</summary>
    Task RefreshFromHubAsync(CancellationToken cancellationToken);

    /// <summary>认不出来返回 null。</summary>
    UnityEditor? Probe(string executablePath);
}

public static class UnityEditorList
{
    /// <summary>同一个版本出现多次时前面的赢：手动指定 &gt; 硬盘扫到的 &gt; Hub 记着的。</summary>
    public static IReadOnlyList<UnityEditor> Merge(
        IEnumerable<UnityEditor> manual,
        IEnumerable<UnityEditor> scanned,
        IEnumerable<UnityEditor> recorded)
    {
        var result = new List<UnityEditor>(manual);
        var seen = result.Select(x => (x.Version, x.ArchitectureForDedup)).ToHashSet();

        foreach (var editor in scanned.Concat(recorded))
        {
            if (seen.Add((editor.Version, editor.ArchitectureForDedup)))
            {
                result.Add(editor);
            }
        }

        return result;
    }

    /// <summary>用户记住的那个优先，它已经不在了就退回第一个。</summary>
    public static UnityEditor? Pick(
        IReadOnlyList<UnityEditor> matching,
        IReadOnlyDictionary<string, string> preferred,
        ushort major)
    {
        var remembered = preferred.GetValueOrDefault(major.ToString(CultureInfo.InvariantCulture));

        return matching.FirstOrDefault(x => x.ExecutablePath == remembered) ?? matching.FirstOrDefault();
    }

    /// <summary>项目用的那一版优先，其次用户记住的，再不行用装了的第一个。</summary>
    public static UnityEditor? PickForProject(
        IReadOnlyList<UnityEditor> editors,
        UnityVersion projectVersion,
        IReadOnlyDictionary<string, string> preferred)
    {
        return editors.FirstOrDefault(x => x.Version == projectVersion)
            ?? Pick(
                [.. editors.Where(x => x.MatchesProject(projectVersion))],
                preferred,
                projectVersion.Major)
            ?? editors.FirstOrDefault();
    }
}
