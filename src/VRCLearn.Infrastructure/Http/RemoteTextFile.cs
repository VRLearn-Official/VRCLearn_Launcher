using System.Net;
using System.Text;

namespace VRCLearn.Infrastructure.Http;

public enum RemoteTextOutcome
{
    Fetched,
    NotModified,
    Failed,
}

/// <param name="Detail">没取到时的原因，只用来写日志。</param>
/// <param name="ETag">响应带的 ETag，可用于下一次条件请求。</param>
public sealed record RemoteText(RemoteTextOutcome Outcome, string Text, string Detail, string? ETag = null)
{
    internal static RemoteText Failure(string detail) => new(RemoteTextOutcome.Failed, "", detail);
}

/// <summary>拉一段纯文本。非 2xx 都按失败处理，304 单独标出来。</summary>
public static class RemoteTextFile
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <param name="url">地址无效时同样按未取到处理。</param>
    /// <param name="headers">额外请求头，可为 null。</param>
    /// <param name="maxBytes">正数时限制响应大小，超过直接判失败。</param>
    public static async Task<RemoteText> GetAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null,
        int maxBytes = 0)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(Timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Absolute));

            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }

            using var response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new RemoteText(RemoteTextOutcome.NotModified, "", "", response.Headers.ETag?.ToString());
            }

            response.EnsureSuccessStatusCode();

            var text = maxBytes > 0
                ? await ReadLimitedAsync(response.Content, maxBytes, timeout.Token).ConfigureAwait(false)
                : await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (text is null)
            {
                return RemoteText.Failure($"response is larger than {maxBytes} bytes");
            }

            return new RemoteText(RemoteTextOutcome.Fetched, text, "", response.Headers.ETag?.ToString());
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or IOException
            or UriFormatException
            or InvalidOperationException)
        {
            return RemoteText.Failure(exception.Message);
        }
    }

    private static async Task<string?> ReadLimitedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            return null;
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;

        using var reader = new StreamReader(
            buffer,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
