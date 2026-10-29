using System.Globalization;
using System.IO.Compression;
using VRCLearn.Core.Hashing;
using VRCLearn.Core.IO;

namespace VRCLearn.Core.Projects;

public enum BackupFormat
{
    ZipStore,

    Zip,

    ZipBest,
}

public static class ProjectBackup
{
    public static async Task CreateAsync(
        IFileIo io,
        ProjectContent content,
        Stream destination,
        BackupFormat format,
        IProgress<FileProgress>? progress,
        CancellationToken cancellationToken)
    {
        var total = content.Directories.Count + content.Files.Count;
        var done = 0;

        var level = format switch
        {
            BackupFormat.ZipStore => CompressionLevel.NoCompression,
            BackupFormat.ZipBest => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var directory in content.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            archive.CreateEntry($"{directory}/");
            progress?.Report(new FileProgress(total, ++done));
        }

        foreach (var file in content.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var source = await io.OpenReadOrNullAsync(file, cancellationToken).ConfigureAwait(false);

            if (source is null)
            {
                continue;
            }

            await using (var target = archive.CreateEntry(file, level).Open())
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new FileProgress(total, ++done));
        }
    }

    public const string StampFormat = "yyyy-MM-ddTHH-mm-ss";

    public const int HashLength = 8;

    private const string LegacyArchiveMarker = ".archive";

    public static string FileNameFor(string projectName, string hash, int serial) =>
        $"{projectName}-{hash}-{serial}.zip";

    public static string ShortHash(ReadOnlySpan<byte> digest) =>
        Sha256Hex.FromDigest(digest[..(HashLength / 2)]);

    public static BackupImportInfo DescribeImport(string fileName, DateTime fallbackTime)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var archive = stem.EndsWith(LegacyArchiveMarker, StringComparison.Ordinal);

        if (archive)
        {
            stem = stem[..^LegacyArchiveMarker.Length];
        }

        if (TrySplitLegacyName(stem, out var projectName, out var timestamp))
        {
            return new(projectName, timestamp, archive);
        }

        if (TrySplitHashName(stem, out projectName))
        {
            return new(projectName, fallbackTime, archive);
        }

        return new(stem.Length > 0 ? stem : fileName, fallbackTime, archive);
    }

    private static bool TrySplitLegacyName(string stem, out string projectName, out DateTime timestamp)
    {
        var separator = stem.Length - StampFormat.Length - 1;

        if (separator > 0
            && stem[separator] == '-'
            && DateTime.TryParseExact(
                stem[(separator + 1)..],
                StampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp))
        {
            projectName = stem[..separator];
            return true;
        }

        projectName = "";
        timestamp = default;
        return false;
    }

    private static bool TrySplitHashName(string stem, out string projectName)
    {
        var parts = stem.Split('-');

        if (parts.Length >= 3
            && parts[^2].Length == HashLength
            && parts[^2].All(char.IsAsciiHexDigit)
            && parts[^1].Length > 0
            && parts[^1].All(char.IsAsciiDigit))
        {
            projectName = string.Join('-', parts[..^2]);

            if (projectName.Length > 0)
            {
                return true;
            }
        }

        projectName = "";
        return false;
    }
}

public sealed record BackupImportInfo(string ProjectName, DateTime CreatedAt, bool IsArchive);
