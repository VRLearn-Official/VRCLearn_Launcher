using VRCLearn.Core.IO;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Templates;

namespace VRCLearn.Core.Resources;

public sealed record ResourceImportOutcome(
    int Imported,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Missing);

public static class ResourceImporter
{
    public static async Task<ResourceImportOutcome> ImportAsync(
        IReadOnlyList<string> packagePaths,
        IFileIo destination,
        Func<string, bool> fileExists,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        var failed = new List<string>();
        var missing = new List<string>();
        var done = 0;

        foreach (var path in packagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!fileExists(path))
            {
                missing.Add(path);
            }
            else
            {
                try
                {
                    await using var stream = File.OpenRead(path);

                    await UnityPackageImporter.ImportAsync(destination, stream, cancellationToken)
                        .ConfigureAwait(false);

                    imported++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    failed.Add(path);
                }
            }

            done++;
            progress?.Report(new FileProgress(packagePaths.Count, done));
        }

        return new ResourceImportOutcome(imported, failed, missing);
    }
}
