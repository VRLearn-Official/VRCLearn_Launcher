namespace VRCLearn.Core.Projects;

public interface IBackupStore
{
    string Directory { get; }

    IReadOnlyList<BackupRecord> List();

    Task<BackupRecord> CreateAsync(
        BackupCreateRequest request,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken);

    Stream? OpenOrNull(string absolutePath);

    void Delete(string absolutePath);

    void WriteNote(string absolutePath, string note);

    void PruneAutoBackups(string projectName, int keep);

    void LockAutoBackup(string absolutePath);

    int CountMigratable();

    BackupMigrationResult Migrate(
        string destinationDirectory,
        IProgress<FileProgress>? progress = null);
}
