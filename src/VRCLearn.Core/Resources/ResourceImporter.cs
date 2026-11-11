using VRCLearn.Core.IO;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Templates;

namespace VRCLearn.Core.Resources;

public sealed record ResourceImportOutcome(
    int Imported,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Missing);

public sealed record ResourceImportConflict(
    string Path,
    bool PathExists,
    bool MetaExists);

public static class ResourceImporter
{
    public static async Task<IReadOnlyList<ResourceImportConflict>> FindExistingAsync(
        IReadOnlyList<string> packagePaths,
        IFileIo destination,
        Func<string, bool> fileExists,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken)
    {
        var conflicts = new Dictionary<string, ResourceImportConflict>(StringComparer.OrdinalIgnoreCase);
        var done = 0;

        foreach (var path in packagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fileExists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);

                    var targets = await UnityPackageImporter
                        .ReadTargetsAsync(stream, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var target in targets)
                    {
                        var pathExists = !target.IsDirectory && destination.FileExists(target.Path);
                        var metaExists = target.HasMetadata
                            && destination.FileExists($"{target.Path}.meta");

                        if (!pathExists && !metaExists)
                        {
                            continue;
                        }

                        if (conflicts.TryGetValue(target.Path, out var prior))
                        {
                            conflicts[target.Path] = prior with
                            {
                                PathExists = prior.PathExists || pathExists,
                                MetaExists = prior.MetaExists || metaExists,
                            };
                        }
                        else
                        {
                            conflicts[target.Path] = new ResourceImportConflict(
                                target.Path,
                                pathExists,
                                metaExists);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException and not OutOfMemoryException)
                {
                }
            }

            done++;
            progress?.Report(new FileProgress(packagePaths.Count, done));
        }

        return
        [
            .. conflicts.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase),
        ];
    }

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
                catch (Exception exception) when (
                    exception is not OperationCanceledException and not OutOfMemoryException)
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
