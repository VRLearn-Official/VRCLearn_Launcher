using VRCLearn.Core.IO;
using System.Text.Json;
using VRCLearn.Core.Serialization;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

/// <summary>解析前去掉 UTF-8 BOM；文件缺失或内容为空白时按默认值处理。</summary>
internal static class ProjectJson
{
    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    public static async Task<T> LoadOrDefaultAsync<T>(
        IFileIo io,
        string relativePath,
        Func<T> createDefault,
        CancellationToken cancellationToken)
        where T : class
    {
        var bytes = await io.ReadAllBytesOrNullAsync(relativePath, cancellationToken).ConfigureAwait(false);

        if (bytes is null || IsBlank(bytes))
        {
            return createDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(Content(bytes), VpmJson.Options) ?? createDefault();
        }
        catch (Exception exception) when (exception is JsonException or VersionParseException)
        {
            throw new InvalidDataException($"syntax error loading {relativePath}: {exception.Message}", exception);
        }
    }

    /// <summary>读取并反序列化；文件缺失或内容为空白时返回 null，解析失败则抛出。</summary>
    public static async Task<T?> ReadOrNullAsync<T>(
        IFileIo io,
        string relativePath,
        CancellationToken cancellationToken)
        where T : class
    {
        var bytes = await io.ReadAllBytesOrNullAsync(relativePath, cancellationToken).ConfigureAwait(false);

        if (bytes is null || IsBlank(bytes))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(Content(bytes), VpmJson.Options);
    }

    public static async Task SaveAsync<T>(
        IFileIo io,
        string relativePath,
        T value,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, VpmJson.Options);
        await io.WriteAllBytesAsync(relativePath, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static ReadOnlySpan<byte> Content(byte[] bytes)
    {
        var span = bytes.AsSpan();

        return span.StartsWith(Bom) ? span[Bom.Length..] : span;
    }

    private static bool IsBlank(byte[] bytes) =>
        bytes.All(x => x is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0);
}
