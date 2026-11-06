namespace VRCLearn.Core.Vpm;

/// <summary>索引对应设置文件中 userRepos 的下标。</summary>
public sealed record RepositoryEntry(int Index, string? Id, Uri? Url, string? Name, string LocalPath);

public enum AddRepositoryStatus
{
    Success,
    AlreadyAdded,
    Unreachable,
    InvalidContent,

    /// <summary>内容取到了，但写不进磁盘。</summary>
    SaveFailed,
}

public sealed record AddRepositoryOutcome(AddRepositoryStatus Status, string? Detail = null);

public enum RemoveRepositoryStatus
{
    Success,
    IndexOutOfRange,
    Stale,

    /// <summary>设置写不进磁盘，条目已放回原位。</summary>
    SaveFailed,
}

public enum ReorderRepositoryStatus
{
    Success,

    Stale,

    /// <summary>设置写不进磁盘，顺序已恢复。</summary>
    SaveFailed,
}

public static class RepositoryMove
{
    public static int Target(int from, int insertBefore) =>
        insertBefore > from ? insertBefore - 1 : insertBefore;
}

public sealed record ImportFromUrlOutcome(ImportSummary? Summary, string? Detail = null);

public sealed record RefreshSummary(int Updated, int NotModified, int Failed);

public sealed record ImportSummary(int Added, int AlreadyAdded, int Failed, int UnparseableLines);
