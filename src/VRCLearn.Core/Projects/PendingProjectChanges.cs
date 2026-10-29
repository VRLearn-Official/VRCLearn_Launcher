using VRCLearn.Core.Packages;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

/// <summary>一个包为什么被移除。</summary>
public enum RemoveReason
{
    Requested,

    /// <summary>被更新的包声明为 legacy。</summary>
    Legacy,

    Unused,
}

public abstract class PackageChange
{
    public Install? AsInstall => this as Install;

    public Remove? AsRemove => this as Remove;
}

/// <summary>装一个包。<see cref="Package"/> 为 null 表示只往清单里加个依赖，装哪个版本交给解析定。</summary>
public sealed class Install(PackageInfo? package, bool addToLocked, DependencyRange? toDependencies)
    : PackageChange
{
    public PackageInfo? Package { get; internal set; } = package;

    public bool AddToLocked { get; internal set; } = addToLocked;

    public DependencyRange? ToDependencies { get; internal set; } = toDependencies;
}

public sealed class Remove(RemoveReason reason) : PackageChange
{
    public RemoveReason Reason { get; } = reason;
}

public sealed class ConflictInfo
{
    private readonly List<string> _conflictingPackages = [];
    private readonly List<string> _unlockedNames = [];

    public IReadOnlyList<string> ConflictingPackages => _conflictingPackages;

    public bool ConflictsWithUnity { get; internal set; }

    /// <summary>占着同一个目录名、但没登记进清单的包。</summary>
    public IReadOnlyList<string> UnlockedNames => _unlockedNames;

    internal void AddConflicting(IEnumerable<string> packages) => _conflictingPackages.AddRange(packages);

    internal void AddUnlocked(string name) => _unlockedNames.Add(name);
}

/// <summary>项目内相对路径，以及声明它的包。</summary>
public sealed record LegacyAsset(string Path, string PackageName);

public sealed class PendingProjectChanges
{
    internal PendingProjectChanges(
        OrderedDictionary<string, PackageChange> packageChanges,
        OrderedDictionary<string, ConflictInfo> conflicts,
        IReadOnlyList<LegacyAsset> removeLegacyFiles,
        IReadOnlyList<LegacyAsset> removeLegacyFolders)
    {
        PackageChanges = packageChanges;
        Conflicts = conflicts;
        RemoveLegacyFiles = removeLegacyFiles;
        RemoveLegacyFolders = removeLegacyFolders;
    }

    public OrderedDictionary<string, PackageChange> PackageChanges { get; }

    public OrderedDictionary<string, ConflictInfo> Conflicts { get; }

    public IReadOnlyList<LegacyAsset> RemoveLegacyFiles { get; }

    public IReadOnlyList<LegacyAsset> RemoveLegacyFolders { get; }
}
