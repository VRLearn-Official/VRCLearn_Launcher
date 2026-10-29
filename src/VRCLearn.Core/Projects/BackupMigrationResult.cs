namespace VRCLearn.Core.Projects;

public enum BackupMigrationStatus
{
    Migrated,
    NothingToMove,
    OverlappingPaths,
    Failed,
}

public sealed record BackupMigrationResult(
    BackupMigrationStatus Status,
    long Bytes = 0,
    int Files = 0,
    string? Error = null);
