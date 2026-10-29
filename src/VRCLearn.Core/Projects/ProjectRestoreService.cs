using System.IO.Compression;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.IO;

namespace VRCLearn.Core.Projects;

/// <param name="SafetyBackup">安全备份的绝对路径。</param>
/// <param name="LeftoverPath">删干净了就是 null。</param>
public sealed record RestoreOutcome(string SafetyBackup, string? LeftoverPath);

public sealed class ProjectRestoreService(
    IBackupStore store,
    ProjectBackupService backups,
    IFileIoFactory ioFactory)
{
    private const int NameAttempts = 100;

    /// <summary>把备份解压成一个新目录，重名时加后缀。返回解压出来的完整路径。</summary>
    public async Task<string> ExtractNewAsync(
        string backupFilePath,
        string parentDirectory,
        string name,
        CancellationToken cancellationToken,
        IProgress<FileProgress>? progress = null)
    {
        await using var source = store.OpenOrNull(backupFilePath)
            ?? throw new FileNotFoundException($"备份文件不在了：{backupFilePath}", backupFilePath);

        DiskSpace.Ensure(parentDirectory, UncompressedSize(source));

        var parent = ioFactory.For(parentDirectory);
        var target = FreeName(parent, name);

        try
        {
            parent.CreateDirectory(target);
            await SafeZipExtractor
                .ExtractAsync(source, parent, target, cancellationToken, progress)
                .ConfigureAwait(false);
        }
        catch
        {
            parent.DeleteDirectory(target);
            throw;
        }

        return Path.Combine(parentDirectory, target);
    }

    private static long UncompressedSize(Stream zip)
    {
        long total;

        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true))
        {
            total = archive.Entries.Sum(x => x.Length);
        }

        if (zip.CanSeek)
        {
            zip.Position = 0;
        }

        return total;
    }

    private static string FreeName(IFileIo parent, string name)
    {
        if (!parent.DirectoryExists(name))
        {
            return name;
        }

        for (var i = 2; i < NameAttempts; i++)
        {
            var candidate = $"{name}-{i}";

            if (!parent.DirectoryExists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"试了 {NameAttempts} 个名字都已经被占了：{name}");
    }

    public async Task<RestoreOutcome> RestoreAsync(
        UnityProject project,
        string projectName,
        string backupFilePath,
        BackupFormat format,
        DateTime timestamp,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var source = store.OpenOrNull(backupFilePath)
            ?? throw new FileNotFoundException($"备份文件不在了：{backupFilePath}", backupFilePath);

        var required = UncompressedSize(source);

        var projectDirectory = Path.TrimEndingDirectorySeparator(project.Io.RootDirectory);

        if (Path.GetDirectoryName(projectDirectory) is not { Length: > 0 } parentPath)
        {
            throw new IOException($"项目没有上级目录，换不了：{projectDirectory}");
        }

        var parent = ioFactory.For(parentPath);
        var name = Path.GetFileName(projectDirectory);
        var stamp = timestamp.ToString(ProjectBackup.StampFormat);

        var safety = await backups
            .CreateAsync(
                project,
                projectName,
                format,
                timestamp,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        var staging = $"{name}.restoring-{stamp}";
        var replaced = $"{name}.replaced-{stamp}";

        DiskSpace.Ensure(parentPath, required);

        try
        {
            parent.CreateDirectory(staging);
            await SafeZipExtractor
                .ExtractAsync(source, parent, staging, cancellationToken, progress)
                .ConfigureAwait(false);
        }
        catch
        {
            parent.DeleteDirectory(staging);
            throw;
        }

        parent.MoveDirectory(name, replaced);

        try
        {
            parent.MoveDirectory(staging, name);
        }
        catch
        {
            parent.MoveDirectory(replaced, name);
            throw;
        }

        string? leftover = null;

        try
        {
            parent.DeleteDirectory(replaced);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            leftover = Path.Combine(parentPath, replaced);
        }

        return new RestoreOutcome(safety.FilePath, leftover);
    }
}
