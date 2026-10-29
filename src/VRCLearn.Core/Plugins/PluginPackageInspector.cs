using System.IO.Compression;
using System.Security.Cryptography;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Hashing;

namespace VRCLearn.Core.Plugins;

public sealed record PluginPackageInspection(
    PluginManifest Manifest,
    string ManifestEntryName,
    IReadOnlyList<string> Entries,
    long UncompressedBytes,
    long CompressedBytes);

public static class PluginPackageInspector
{
    public const int MaxEntries = 2000;

    public const long MaxTotalUncompressedBytes = 256L * 1024 * 1024;

    public const long MaxEntryUncompressedBytes = 128L * 1024 * 1024;

    public const long MaxManifestBytes = 256L * 1024;

    public const int MaxCompressionRatio = 200;

    private static readonly HashSet<string> HostAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "VRCLearn.Plugin.Sdk",
        "VRCLearn.Core",
        "VRCLearn.Infrastructure",
        "VRCLearnLauncher",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "Wpf.Ui",
        "Wpf.Ui.Abstractions",
        "CommunityToolkit.Mvvm",
    };

    public static PluginPackageInspection Inspect(Stream stream)
    {
        if (!stream.CanSeek)
        {
            throw new PluginPackageException("plugin package stream must be seekable");
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        if (archive.Entries.Count > MaxEntries)
        {
            throw new PluginPackageException($"plugin package has too many entries: {archive.Entries.Count}");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<string>();
        var manifests = new List<ZipArchiveEntry>();
        long totalUncompressed = 0;
        long totalCompressed = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains(':') || IsSymbolicLink(entry))
            {
                throw new PluginPackageException($"unsafe zip entry: {entry.FullName}");
            }

            var normalized = ZipEntryPath.Normalize(entry.FullName);

            if (normalized is null)
            {
                throw new PluginPackageException($"unsafe zip entry: {entry.FullName}");
            }

            if (!paths.Add(normalized))
            {
                throw new PluginPackageException($"duplicate zip entry: {normalized}");
            }

            entries.Add(normalized);

            if (normalized.Equals(PluginManifestReader.FileName, StringComparison.OrdinalIgnoreCase))
            {
                manifests.Add(entry);
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            totalUncompressed += entry.Length;
            totalCompressed += entry.CompressedLength;

            if (entry.Length > MaxEntryUncompressedBytes)
            {
                throw new PluginPackageException($"zip entry is too large: {normalized}");
            }

            if (entry.Length > 1024 * 1024
                && entry.CompressedLength > 0
                && entry.Length / entry.CompressedLength > MaxCompressionRatio)
            {
                throw new PluginPackageException($"zip entry has an excessive compression ratio: {normalized}");
            }

            if (HostAssemblies.Contains(Path.GetFileNameWithoutExtension(normalized))
                || Path.GetFileNameWithoutExtension(normalized)
                    .StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginPackageException($"plugin must not bundle host assembly: {normalized}");
            }
        }

        if (totalUncompressed > MaxTotalUncompressedBytes)
        {
            throw new PluginPackageException($"plugin package is too large: {totalUncompressed} bytes");
        }

        if (manifests.Count != 1)
        {
            throw new PluginPackageException("plugin package must contain exactly one plugin.json at the root");
        }

        var manifestEntry = manifests[0];

        if (manifestEntry.Length > MaxManifestBytes)
        {
            throw new PluginPackageException("plugin.json is too large");
        }

        PluginManifest manifest;

        using (var manifestStream = manifestEntry.Open())
        {
            manifest = PluginManifestReader.Read(manifestStream);
        }

        return new PluginPackageInspection(
            manifest,
            manifestEntry.FullName,
            entries,
            totalUncompressed,
            totalCompressed);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Sha256Hex.FromDigest(digest);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
}
