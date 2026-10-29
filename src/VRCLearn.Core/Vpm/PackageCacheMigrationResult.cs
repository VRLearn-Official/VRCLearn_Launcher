namespace VRCLearn.Core.Vpm;

public enum PackageCacheMigrationStatus
{
    Migrated,
    NothingToMove,
    UnsafeDestination,
    OverlappingPaths,
    Failed,
}

public sealed record PackageCacheMigrationResult(
    PackageCacheMigrationStatus Status,
    long Bytes = 0,
    string? Error = null);
