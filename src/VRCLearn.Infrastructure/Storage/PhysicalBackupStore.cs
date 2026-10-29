using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.IO;

namespace VRCLearn.Infrastructure.Storage;

public sealed class PhysicalBackupStore(
    ILauncherSettingsStore settings,
    AppPaths paths,
    GuiConfigStore configs) : IBackupStore
{
    private readonly Lock _gate = new();

    public string Directory =>
        string.IsNullOrWhiteSpace(settings.Current.ProjectBackupPath)
            ? paths.BackupDirectory
            : settings.Current.ProjectBackupPath;

    public IReadOnlyList<BackupRecord> List()
    {
        lock (_gate)
        {
            return [.. configs.Current.BackupRecords.OrderByDescending(x => x.CreatedAt)];
        }
    }

    public async Task<BackupRecord> CreateAsync(
        BackupCreateRequest request,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(Directory);
        System.IO.Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        string? finalPath = null;
        var moved = false;

        try
        {
            byte[] digest;

            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (var file = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    using var hashing = new HashingWriteStream(file, hash);

                    await write(hashing, cancellationToken).ConfigureAwait(false);
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                digest = hash.GetHashAndReset();
            }

            var shortHash = ProjectBackup.ShortHash(digest);

            lock (_gate)
            {
                var serial = NextSerial(request.ProjectName);
                finalPath = NextFreePath(directory, request.ProjectName, shortHash, ref serial);

                File.Move(tempPath, finalPath);
                moved = true;

                var record = new BackupRecord
                {
                    FilePath = finalPath,
                    ProjectName = request.ProjectName,
                    CreatedAt = request.CreatedAt,
                    IsArchive = request.IsArchive,
                    IsAuto = request.IsAuto,
                    Serial = serial,
                    Note = request.Note?.Trim() ?? "",
                };

                configs.Current.BackupRecords.Add(record);
                configs.Save();

                return record;
            }
        }
        catch
        {
            FileCleanup.TryDelete(tempPath);

            if (moved && finalPath is not null)
            {
                FileCleanup.TryDelete(finalPath);
            }

            throw;
        }
    }

    public Stream? OpenOrNull(string absolutePath) =>
        !string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath)
            ? File.OpenRead(absolutePath)
            : null;

    public void Delete(string absolutePath)
    {
        Recycle(absolutePath);

        lock (_gate)
        {
            if (configs.Current.BackupRecords.RemoveAll(x => SamePath(x.FilePath, absolutePath)) > 0)
            {
                configs.Save();
            }
        }
    }

    public void WriteNote(string absolutePath, string note)
    {
        lock (_gate)
        {
            var record = configs.Current.BackupRecords.FirstOrDefault(x => SamePath(x.FilePath, absolutePath));

            if (record is null)
            {
                return;
            }

            var text = note.Trim();

            if (record.Note == text)
            {
                return;
            }

            record.Note = text;
            configs.Save();
        }
    }

    public void PruneAutoBackups(string projectName, int keep)
    {
        var limit = Math.Max(0, keep);

        lock (_gate)
        {
            var auto = configs.Current.BackupRecords
                .Where(x => x.IsAuto
                    && string.Equals(x.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var changed = false;

            foreach (var record in auto.Where(x => !File.Exists(x.FilePath)).ToList())
            {
                configs.Current.BackupRecords.Remove(record);
                changed = true;
            }

            var stale = auto
                .Where(x => File.Exists(x.FilePath))
                .OrderByDescending(x => x.CreatedAt)
                .Skip(limit)
                .ToList();

            foreach (var record in stale)
            {
                try
                {
                    if (File.Exists(record.FilePath))
                    {
                        File.Delete(record.FilePath);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                configs.Current.BackupRecords.Remove(record);
                changed = true;
            }

            if (changed)
            {
                configs.Save();
            }
        }
    }

    public void LockAutoBackup(string absolutePath)
    {
        lock (_gate)
        {
            var record = configs.Current.BackupRecords.FirstOrDefault(x => SamePath(x.FilePath, absolutePath));

            if (record is null || !record.IsAuto)
            {
                return;
            }

            record.IsAuto = false;
            record.Note = "";
            configs.Save();
        }
    }

    public int CountMigratable()
    {
        lock (_gate)
        {
            try
            {
                return Migratable(PathUtil.Normalize(Directory)).Count();
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return 0;
            }
        }
    }

    public BackupMigrationResult Migrate(
        string destinationDirectory,
        IProgress<FileProgress>? progress = null)
    {
        string source;
        string target;

        try
        {
            source = PathUtil.Normalize(Directory);
            target = PathUtil.Normalize(destinationDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new BackupMigrationResult(BackupMigrationStatus.Failed, Error: exception.Message);
        }

        if (PathUtil.SamePath(source, target))
        {
            return new BackupMigrationResult(BackupMigrationStatus.NothingToMove);
        }

        if (PathUtil.IsUnder(source, target) || PathUtil.IsUnder(target, source))
        {
            return new BackupMigrationResult(BackupMigrationStatus.OverlappingPaths);
        }

        List<BackupRecord> records;

        lock (_gate)
        {
            records = [.. Migratable(source)];
        }

        if (records.Count == 0)
        {
            return new BackupMigrationResult(BackupMigrationStatus.NothingToMove);
        }

        var bytes = 0L;
        var moved = 0;

        try
        {
            System.IO.Directory.CreateDirectory(target);

            foreach (var record in records)
            {
                var destination = Path.Combine(target, Path.GetFileName(record.FilePath));

                bytes += new FileInfo(record.FilePath).Length;
                FileMover.Move(record.FilePath, destination);

                lock (_gate)
                {
                    record.FilePath = destination;
                }

                moved++;
                progress?.Report(new FileProgress(records.Count, moved));
            }

            lock (_gate)
            {
                configs.Save();
            }

            return new BackupMigrationResult(BackupMigrationStatus.Migrated, bytes, moved);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                configs.Save();
            }

            return new BackupMigrationResult(BackupMigrationStatus.Failed, bytes, moved, exception.Message);
        }
    }

    private IEnumerable<BackupRecord> Migratable(string directory) =>
        configs.Current.BackupRecords.Where(x => IsInDirectory(x.FilePath, directory));

    private static bool IsInDirectory(string path, string directory)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));

            return parent is not null && PathUtil.SamePath(parent, directory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private int NextSerial(string projectName) =>
        configs.Current.BackupRecords
            .Where(x => string.Equals(x.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Serial)
            .DefaultIfEmpty(0)
            .Max() + 1;

    private static string NextFreePath(string directory, string projectName, string hash, ref int serial)
    {
        while (true)
        {
            var path = Path.Combine(directory, ProjectBackup.FileNameFor(projectName, hash, serial));

            if (!File.Exists(path))
            {
                return path;
            }

            serial++;
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void Recycle(string path)
    {
        if (File.Exists(path))
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

}
