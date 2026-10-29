using VRCLearn.Core.Configuration;

namespace VRCLearn.Core.Projects;

public sealed class ProjectBackupService(IBackupStore store)
{
    public Task<BackupRecord> CreateAsync(
        UnityProject project,
        string projectName,
        BackupFormat format,
        DateTime timestamp,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken,
        bool archive = false,
        string? note = null,
        bool auto = false)
    {
        var content = ProjectFiles.Collect(project, ProjectFileFilter.ForBackup());

        DiskSpace.Ensure(store.Directory, EstimateBytes(project, content));

        var request = new BackupCreateRequest(projectName, timestamp, archive, note, auto);

        return store.CreateAsync(
            request,
            (destination, token) => ProjectBackup.CreateAsync(
                project.Io,
                content,
                destination,
                format,
                progress,
                token),
            cancellationToken);
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
