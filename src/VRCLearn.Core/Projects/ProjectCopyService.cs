using VRCLearn.Core.IO;

namespace VRCLearn.Core.Projects;

public sealed class ProjectCopyService(IFileIoFactory fileIo)
{
    private const int SiblingAttempts = 100;

    public async Task CopyAsync(
        UnityProject project,
        string destinationDirectory,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken)
    {
        var content = ProjectFiles.Collect(
            project,
            new ProjectFileFilter(ExcludeLibrary: false));

        var destination = fileIo.For(destinationDirectory);
        var total = content.Directories.Count + content.Files.Count;
        var done = 0;

        destination.CreateDirectory("");

        foreach (var directory in content.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            destination.CreateDirectory(directory);
            progress?.Report(new FileProgress(total, ++done));
        }

        foreach (var file in content.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var source = await project.Io
                .OpenReadOrNullAsync(file, cancellationToken)
                .ConfigureAwait(false);

            if (source is null)
            {
                continue;
            }

            await using (var target = await destination.CreateAsync(file, cancellationToken).ConfigureAwait(false))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new FileProgress(total, ++done));
        }
    }

    public static string FreeSiblingPath(IFileIo parent, string name)
    {
        for (var i = 0; i < SiblingAttempts; i++)
        {
            var candidate = i == 0 ? $"{name}-Migrated" : $"{name}-Migrated-{i}";

            if (!parent.DirectoryExists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"试了 {SiblingAttempts} 个名字都已经被占了：{name}-Migrated");
    }
}
