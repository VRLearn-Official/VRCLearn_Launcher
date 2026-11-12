using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.IO;

namespace VRCLearn.Core.Projects;

/// <param name="Leftovers">没能删掉的临时目录，删干净了就是空列表。</param>
public sealed record RestoreOutcome(IReadOnlyList<string> Leftovers);

/// <param name="Detail">恢复失败时的原因；成功但还有残留时为空。</param>
public sealed record RestoreRecovery(string ProjectPath, bool Recovered, string? Detail = null);

public sealed class ProjectRestoreService(
    IBackupStore store,
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

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var root = UnityProjectRootLocator.Locate(archive.Entries.Select(x => x.FullName));
        var plan = SafeZipExtractor.Plan(archive, root);

        DiskSpace.Ensure(parentDirectory, plan.UncompressedBytes);

        var parent = ioFactory.For(parentDirectory);
        var target = FreeName(parent, name);

        try
        {
            parent.CreateDirectory(target);

            await SafeZipExtractor
                .ExtractAsync(plan, parent, target, cancellationToken, progress)
                .ConfigureAwait(false);

            if (!parent.FileExists($"{target}/{UnityProject.ProjectVersionPath}"))
            {
                throw new InvalidDataException(
                    "还原内容里没有找到 ProjectSettings/ProjectVersion.txt，已取消还原。");
            }
        }
        catch
        {
            parent.DeleteDirectory(target);
            throw;
        }

        return Path.Combine(parentDirectory, target);
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

    private sealed record RestoreJournal(string Staging, string Replaced);

    private static string JournalName(string name) => $"{name}.vrclearn-restore.json";

    private static async Task WriteJournalAsync(
        IFileIo parent,
        string name,
        string staging,
        string replaced,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new RestoreJournal(staging, replaced), JsonDefaults.Compact);

        await parent
            .WriteAllBytesAsync(JournalName(name), Encoding.UTF8.GetBytes(json), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void DeleteJournal(IFileIo parent, string name) => parent.DeleteFile(JournalName(name));

    private static void TryDelete(IFileIo parent, string relativePath, string parentPath, List<string> leftovers)
    {
        try
        {
            parent.DeleteDirectory(relativePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            leftovers.Add(Path.Combine(parentPath, relativePath));
        }
    }

    private static Exception WithLeftovers(Exception cause, List<string> leftovers) =>
        leftovers.Count == 0
            ? cause
            : new IOException(
                $"{cause.Message}；临时目录删不掉，留在：{string.Join("、", leftovers)}",
                cause);

    public async Task<RestoreOutcome> RestoreAsync(
        UnityProject project,
        string backupFilePath,
        DateTime timestamp,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var source = store.OpenOrNull(backupFilePath)
            ?? throw new FileNotFoundException($"备份文件不在了：{backupFilePath}", backupFilePath);

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var root = UnityProjectRootLocator.Locate(archive.Entries.Select(x => x.FullName));
        var plan = SafeZipExtractor.Plan(archive, root);
        var required = plan.UncompressedBytes;

        var projectDirectory = Path.TrimEndingDirectorySeparator(project.Io.RootDirectory);

        if (Path.GetDirectoryName(projectDirectory) is not { Length: > 0 } parentPath)
        {
            throw new IOException($"项目没有上级目录，换不了：{projectDirectory}");
        }

        var parent = ioFactory.For(parentPath);
        var name = Path.GetFileName(projectDirectory);
        var stamp = timestamp.ToString(ProjectBackup.StampFormat);

        var staging = FreeName(parent, $"{name}.restoring-{stamp}");
        var replaced = FreeName(parent, $"{name}.replaced-{stamp}");
        var leftovers = new List<string>();

        DiskSpace.Ensure(parentPath, required);

        try
        {
            parent.CreateDirectory(staging);

            await SafeZipExtractor
                .ExtractAsync(plan, parent, staging, cancellationToken, progress)
                .ConfigureAwait(false);

            if (!parent.FileExists($"{staging}/{UnityProject.ProjectVersionPath}"))
            {
                throw new InvalidDataException(
                    "还原内容里没有找到 ProjectSettings/ProjectVersion.txt，已取消还原，原项目未改动。");
            }
        }
        catch (Exception exception)
        {
            TryDelete(parent, staging, parentPath, leftovers);
            throw WithLeftovers(exception, leftovers);
        }

        try
        {
            await WriteJournalAsync(parent, name, staging, replaced, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TryDelete(parent, staging, parentPath, leftovers);
            throw WithLeftovers(exception, leftovers);
        }

        try
        {
            parent.MoveDirectory(name, replaced);
        }
        catch (Exception exception)
        {
            DeleteJournal(parent, name);
            TryDelete(parent, staging, parentPath, leftovers);
            throw WithLeftovers(exception, leftovers);
        }

        try
        {
            parent.MoveDirectory(staging, name);
        }
        catch (Exception moveException)
        {
            try
            {
                parent.MoveDirectory(replaced, name);
                DeleteJournal(parent, name);
                TryDelete(parent, staging, parentPath, leftovers);
            }
            catch (Exception rollbackException)
            {
                leftovers.Add(Path.Combine(parentPath, replaced));
                leftovers.Add(Path.Combine(parentPath, staging));

                throw new IOException(
                    $"恢复失败，原项目目录也无法移回，已保留在：{Path.Combine(parentPath, replaced)}",
                    rollbackException);
            }

            if (leftovers.Count > 0)
            {
                throw new IOException(
                    $"{moveException.Message}；临时目录删不掉，留在：{string.Join("、", leftovers)}",
                    moveException);
            }

            throw;
        }

        DeleteJournal(parent, name);
        TryDelete(parent, replaced, parentPath, leftovers);

        return new RestoreOutcome(leftovers);
    }

    /// <summary>启动时把上次没做完的目录交换恢复掉。</summary>
    public async Task<IReadOnlyList<RestoreRecovery>> RecoverInterruptedAsync(
        IEnumerable<string> projectPaths,
        CancellationToken cancellationToken)
    {
        var recoveries = new List<RestoreRecovery>();

        foreach (var projectPath in projectPaths)
        {
            var normalized = Path.TrimEndingDirectorySeparator(projectPath);
            var name = Path.GetFileName(normalized);

            if (name.Length == 0 || Path.GetDirectoryName(normalized) is not { Length: > 0 } parentPath)
            {
                continue;
            }

            var parent = ioFactory.For(parentPath);
            var journalName = JournalName(name);

            if (!parent.FileExists(journalName)
                || await parent.ReadAllBytesOrNullAsync(journalName, cancellationToken).ConfigureAwait(false) is not { } bytes)
            {
                continue;
            }

            RestoreJournal? journal;

            try
            {
                journal = JsonSerializer.Deserialize<RestoreJournal>(bytes, JsonDefaults.Compact);
            }
            catch (JsonException exception)
            {
                recoveries.Add(new RestoreRecovery(projectPath, false, exception.Message));

                continue;
            }

            if (journal is null)
            {
                recoveries.Add(new RestoreRecovery(projectPath, false, "恢复日志无法解析"));

                continue;
            }

            try
            {
                var recovered = false;

                if (!parent.DirectoryExists(name) && parent.DirectoryExists(journal.Replaced))
                {
                    parent.MoveDirectory(journal.Replaced, name);
                    recovered = true;
                }

                if (!parent.DirectoryExists(name))
                {
                    recoveries.Add(new RestoreRecovery(projectPath, false, "原目录和替换目录都不在，保留恢复日志"));

                    continue;
                }

                if (parent.DirectoryExists(journal.Staging))
                {
                    parent.DeleteDirectory(journal.Staging);
                }

                DeleteJournal(parent, name);

                var leftover = parent.DirectoryExists(journal.Replaced)
                    ? $"旧目录留在 {Path.Combine(parentPath, journal.Replaced)}"
                    : null;

                recoveries.Add(new RestoreRecovery(projectPath, recovered, leftover));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                recoveries.Add(new RestoreRecovery(projectPath, false, exception.Message));
            }
        }

        return recoveries;
    }
}
