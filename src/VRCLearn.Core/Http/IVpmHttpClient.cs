namespace VRCLearn.Core.Http;

public sealed record HttpFetchResult(Stream Stream, string? ETag);

public interface IVpmHttpClient
{
    /// <summary>服务端返回错误状态码时应抛出异常。</summary>
    Task<Stream> GetAsync(
        Uri url,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken);

    /// <summary>返回 null 表示缓存仍然有效（服务端 304）。</summary>
    Task<HttpFetchResult?> GetWithETagAsync(
        Uri url,
        IReadOnlyDictionary<string, string> headers,
        string? currentETag,
        CancellationToken cancellationToken);
}
