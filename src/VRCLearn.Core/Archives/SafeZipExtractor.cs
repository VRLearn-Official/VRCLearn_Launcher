using System.Buffers;
using System.IO.Compression;
using VRCLearn.Core.IO;
using VRCLearn.Core.Projects;

namespace VRCLearn.Core.Archives;

public sealed class ZipExtractionPlan
{
    internal ZipExtractionPlan(
        string? rootPrefix,
        IReadOnlyList<PlannedZipEntry> entries,
        int fileCount,
        long uncompressedBytes)
    {
        RootPrefix = rootPrefix;
        Entries = entries;
        FileCount = fileCount;
        UncompressedBytes = uncompressedBytes;
    }

    public string? RootPrefix { get; }

    public int FileCount { get; }

    public long UncompressedBytes { get; }

    internal IReadOnlyList<PlannedZipEntry> Entries { get; }
}

internal readonly record struct PlannedZipEntry(ZipArchiveEntry Entry, string RelativePath, bool IsDirectory);

public static class SafeZipExtractor
{
    public static async Task ExtractAsync(
        Stream zip,
        IFileIo io,
        string destinationRelativePath,
        CancellationToken cancellationToken,
        IProgress<FileProgress>? progress = null,
        long maxEntryBytes = 0,
        long maxTotalBytes = 0)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);

        await ExtractAsync(
                Plan(archive, null),
                io,
                destinationRelativePath,
                cancellationToken,
                progress,
                maxEntryBytes,
                maxTotalBytes)
            .ConfigureAwait(false);
    }

    public static ZipExtractionPlan Plan(ZipArchive archive, string? rootPrefix)
    {
        string? prefix = null;

        if (!string.IsNullOrEmpty(rootPrefix))
        {
            prefix = ZipEntryPath.Normalize(rootPrefix)
                ?? throw new ArgumentException($"invalid root prefix: {rootPrefix}", nameof(rootPrefix));
        }

        var entries = new List<PlannedZipEntry>();
        var fileCount = 0;
        long uncompressedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var normalized = ZipEntryPath.Normalize(entry.FullName)
                ?? throw new InvalidDataException($"directory traversal detected: {entry.FullName}");

            string relativePath;

            if (prefix is null)
            {
                relativePath = normalized;
            }
            else if (normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = "";
                isDirectory = true;
            }
            else if (normalized.Length > prefix.Length
                && normalized[prefix.Length] == '/'
                && normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = normalized[(prefix.Length + 1)..];
            }
            else
            {
                continue;
            }

            entries.Add(new PlannedZipEntry(entry, relativePath, isDirectory));

            if (!isDirectory)
            {
                fileCount++;
                uncompressedBytes += entry.Length;
            }
        }

        return new ZipExtractionPlan(prefix, entries, fileCount, uncompressedBytes);
    }

    public static async Task ExtractAsync(
        ZipExtractionPlan plan,
        IFileIo io,
        string destinationRelativePath,
        CancellationToken cancellationToken,
        IProgress<FileProgress>? progress = null,
        long maxEntryBytes = 0,
        long maxTotalBytes = 0)
    {
        var total = plan.FileCount;
        var done = 0;
        var writtenTotal = 0L;

        foreach (var item in plan.Entries)
        {
            var path = item.RelativePath.Length == 0
                ? destinationRelativePath
                : $"{destinationRelativePath}/{item.RelativePath}";

            if (item.IsDirectory)
            {
                io.CreateDirectory(path);
                continue;
            }

            await using var source = item.Entry.Open();
            await using var target = await io.CreateAsync(path, cancellationToken).ConfigureAwait(false);

            if (maxEntryBytes <= 0 && maxTotalBytes <= 0)
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                writtenTotal += await CopyWithLimitsAsync(
                        source,
                        target,
                        path,
                        maxEntryBytes,
                        maxTotalBytes,
                        writtenTotal,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new FileProgress(total, ++done));
        }
    }

    private static async Task<long> CopyWithLimitsAsync(
        Stream source,
        Stream target,
        string path,
        long maxEntryBytes,
        long maxTotalBytes,
        long writtenTotal,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var written = 0L;

        try
        {
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (maxEntryBytes > 0 && written + read > maxEntryBytes)
                {
                    throw new InvalidDataException($"zip entry is too large: {path}");
                }

                if (maxTotalBytes > 0 && writtenTotal + written + read > maxTotalBytes)
                {
                    throw new InvalidDataException("zip extraction is too large");
                }

                await target
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);

                written += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return written;
    }
}
