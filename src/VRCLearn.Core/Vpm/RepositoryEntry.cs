namespace VRCLearn.Core.Vpm;

/// <summary>索引对应设置文件中 userRepos 的下标。</summary>
public sealed record RepositoryEntry(int Index, string? Id, Uri? Url, string? Name, string LocalPath);

public enum AddRepositoryStatus
{
    Success,
    AlreadyAdded,
    Unreachable,
    InvalidContent,
}

public sealed record AddRepositoryOutcome(AddRepositoryStatus Status, string? Detail = null);

public enum RemoveRepositoryStatus
{
    Success,
    IndexOutOfRange,
    Stale,
}

public enum ReorderRepositoryStatus
{
    Success,

    Stale,
}

public static class RepositoryMove
{
    public static int Target(int from, int insertBefore) =>
        insertBefore > from ? insertBefore - 1 : insertBefore;
}

public sealed record ImportFromUrlOutcome(ImportSummary? Summary, string? Detail = null);

public sealed record RefreshSummary(int Updated, int NotModified, int Failed);

public sealed record ImportSummary(int Added, int AlreadyAdded, int Failed, int UnparseableLines);
