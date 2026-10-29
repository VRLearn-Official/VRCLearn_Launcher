using VRCLearn.Core.IO;
using System.IO.Compression;
using VRCLearn.Core.Projects;

namespace VRCLearn.Core.Archives;

public static class SafeZipExtractor
{
    public static async Task ExtractAsync(
        Stream zip,
        IFileIo io,
        string destinationRelativePath,
        CancellationToken cancellationToken,
        IProgress<FileProgress>? progress = null)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);

        var total = archive.Entries.Count(x => !x.FullName.EndsWith('/') && !x.FullName.EndsWith('\\'));
        var done = 0;

        foreach (var entry in archive.Entries)
        {
            if (ZipEntryPath.Normalize(entry.FullName) is not { } relativePath)
            {
                throw new InvalidDataException($"directory traversal detected: {entry.FullName}");
            }

            var path = $"{destinationRelativePath}/{relativePath}";

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                io.CreateDirectory(path);
                continue;
            }

            await using var source = entry.Open();
            await using var target = await io.CreateAsync(path, cancellationToken).ConfigureAwait(false);

            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);

            progress?.Report(new FileProgress(total, ++done));
        }
    }
}
