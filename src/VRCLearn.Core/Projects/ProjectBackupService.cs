using VRCLearn.Core.Configuration;

namespace VRCLearn.Core.Projects;

public sealed class ProjectBackupService(IBackupStore store)
{
    public async Task<BackupRecord> CreateAsync(
        UnityProject project,
        string projectName,
        BackupFormat format,
        DateTime timestamp,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken,
        bool includeLibrary = false,
        string? note = null,
        ICollection<string>? skippedFiles = null)
    {
        var content = await Task.Run(
            () => ProjectFiles.Collect(project, ProjectFileFilter.ForBackup(includeLibrary)),
            cancellationToken).ConfigureAwait(false);

        var estimated = await Task.Run(
            () => EstimateBytes(project, content),
            cancellationToken).ConfigureAwait(false);

        DiskSpace.Ensure(store.Directory, estimated);

        var request = new BackupCreateRequest(projectName, timestamp, note);

        return await store.CreateAsync(
            request,
            (destination, token) => Task.Run(
                () => ProjectBackup.CreateAsync(
                    project.Io,
                    content,
                    destination,
                    format,
                    progress,
                    token,
                    skipLockedLibraryFiles: includeLibrary,
                    skippedFiles: skippedFiles),
                token),
            cancellationToken).ConfigureAwait(false);
    }

    private static long EstimateBytes(UnityProject project, ProjectContent content)
    {
        var total = 0L;

        foreach (var file in content.Files)
        {
            try
            {
                total += new FileInfo(Path.Combine(project.Io.RootDirectory, file)).Length;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }
        }

        return total;
    }

    public static BackupFormat ParseFormat(string? text) => text switch
    {
        "zip-store" => BackupFormat.ZipStore,
        "zip-best" => BackupFormat.ZipBest,
        _ => BackupFormat.Zip,
    };
}
