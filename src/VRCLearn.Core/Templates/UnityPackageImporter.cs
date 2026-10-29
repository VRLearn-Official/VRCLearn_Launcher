using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using VRCLearn.Core.IO;

namespace VRCLearn.Core.Templates;

public static class UnityPackageImporter
{
    private static readonly char[] BannedCharacters = ['<', '>', ':', '"', '|', '?', '*', '\0'];

    public static async Task ImportAsync(
        IFileIo destination,
        Stream package,
        CancellationToken cancellationToken)
    {
        var entries = await ReadAsync(package, cancellationToken).ConfigureAwait(false);

        foreach (var (_, entry) in entries)
        {
            var path = entry.Pathname!;
            var meta = $"{path}.meta";

            if (entry.Asset is { } asset)
            {
                await destination.WriteAllBytesAsync(path, asset, cancellationToken).ConfigureAwait(false);
                await destination.WriteAllBytesAsync(meta, entry.Metadata!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                destination.CreateDirectory(path);

                if (entry.Metadata is { Length: > 0 })
                {
                    await destination.WriteAllBytesAsync(meta, entry.Metadata, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<Dictionary<string, Entry>> ReadAsync(Stream package, CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var reader = new TarReader(new GZipStream(package, CompressionMode.Decompress), leaveOpen: false);

        await using (reader.ConfigureAwait(false))
        {
            while (reader.GetNextEntry(copyData: true) is { } tarEntry)
            {
                if (!IsAssetEntry(tarEntry.Name, out var guid, out var kind))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!entries.TryGetValue(guid, out var entry))
                {
                    entry = new Entry();
                    entries[guid] = entry;
                }

                await ReadEntryAsync(entry, kind, tarEntry, guid, cancellationToken).ConfigureAwait(false);
            }
        }

        Validate(entries);

        return entries;
    }

    private static async Task ReadEntryAsync(
        Entry entry,
        EntryKind kind,
        TarEntry tarEntry,
        string guid,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case EntryKind.Asset:
                if (entry.Asset is not null)
                {
                    throw new InvalidDataException($"unitypackage 里 {guid} 有两条 asset");
                }

                entry.Asset = await ReadAllAsync(tarEntry, cancellationToken).ConfigureAwait(false);
                break;

            case EntryKind.Metadata:
                if (entry.Metadata is not null)
                {
                    throw new InvalidDataException($"unitypackage 里 {guid} 有两条 asset.meta");
                }

                entry.Metadata = await ReadAllAsync(tarEntry, cancellationToken).ConfigureAwait(false);
                break;

            default:
                if (entry.Pathname is not null)
                {
                    throw new InvalidDataException($"unitypackage 里 {guid} 有两条 pathname");
                }

                entry.Pathname = ParsePathname(await ReadAllAsync(tarEntry, cancellationToken).ConfigureAwait(false), guid);
                break;
        }
    }

    private static async Task<byte[]> ReadAllAsync(TarEntry entry, CancellationToken cancellationToken)
    {
        if (entry.DataStream is not { } stream)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    private static string ParsePathname(byte[] bytes, string guid)
    {
        var pathname = Encoding.UTF8.GetString(bytes).Replace('\\', '/');

        if (pathname.IndexOfAny(BannedCharacters) >= 0)
        {
            throw new InvalidDataException($"unitypackage 里 {guid} 的目标路径含有不能用在文件名里的字符：{pathname}");
        }

        if (pathname.Split('/').Any(x => x is "" or "." or ".."))
        {
            throw new InvalidDataException($"unitypackage 里 {guid} 的目标路径不合法：{pathname}");
        }

        if (!pathname.StartsWith("Assets/", StringComparison.Ordinal)
            && !pathname.StartsWith("Packages/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"unitypackage 里 {guid} 的目标路径不在 Assets/ 或 Packages/ 下：{pathname}");
        }

        var newline = pathname.LastIndexOf('\n');

        return newline < 0 ? pathname : pathname[..newline];
    }

    private static void Validate(Dictionary<string, Entry> entries)
    {
        foreach (var (guid, entry) in entries)
        {
            if (entry.Pathname is not { Length: > 0 })
            {
                throw new InvalidDataException($"unitypackage 里 {guid} 没有 pathname，不知道要铺到哪儿");
            }

            if (entry.Asset is not null && entry.Metadata is not { Length: > 0 })
            {
                throw new InvalidDataException($"unitypackage 里 {guid}（{entry.Pathname}）没有 asset.meta");
            }
        }
    }

    /// <summary>只认 <c>&lt;32 位十六进制的 guid&gt;/&lt;asset | asset.meta | pathname&gt;</c>。</summary>
    private static bool IsAssetEntry(string name, out string guid, out EntryKind kind)
    {
        kind = EntryKind.Other;
        guid = "";

        var components = new List<string>();

        foreach (var component in name.Split('/', '\\'))
        {
            switch (component)
            {
                case "" or ".":
                    continue;
                case "..":
                    if (components.Count > 0)
                    {
                        components.RemoveAt(components.Count - 1);
                    }

                    continue;
                default:
                    components.Add(component);
                    break;
            }
        }

        if (components is not [var candidate, var fileName] || !IsGuid(candidate))
        {
            return false;
        }

        kind = fileName switch
        {
            "asset" => EntryKind.Asset,
            "asset.meta" => EntryKind.Metadata,
            "pathname" => EntryKind.Pathname,
            _ => EntryKind.Other,
        };

        if (kind == EntryKind.Other)
        {
            return false;
        }

        guid = candidate;
        return true;
    }

    private static bool IsGuid(string value) =>
        value.Length == 32 && value.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');

    private enum EntryKind
    {
        Other,
        Asset,
        Metadata,
        Pathname,
    }

    private sealed class Entry
    {
        public byte[]? Asset { get; set; }

        public byte[]? Metadata { get; set; }

        public string? Pathname { get; set; }
    }
}
