using System.Formats.Tar;
using System.IO.Compression;

namespace VRCLearn.Infrastructure.ProjectTemplates;

internal static class TarGzEntryReader
{
    private const int BufferSize = 8192;

    public static async Task<byte[]?> ReadEntryAsync(
        Stream archive,
        string entryPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var target = Normalize(entryPath);

        using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new TarReader(gzip, leaveOpen: true);

        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Normalize(entry.Name) != target || entry.DataStream is null)
            {
                continue;
            }

            if (entry.Length > maxBytes)
            {
                throw new InvalidDataException($"{entryPath} 超过大小限制");
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[BufferSize];
            var total = 0L;
            int read;

            while ((read = await entry.DataStream
                       .ReadAsync(chunk, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                total += read;

                if (total > maxBytes)
                {
                    throw new InvalidDataException($"{entryPath} 超过大小限制");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        return null;
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }
}
