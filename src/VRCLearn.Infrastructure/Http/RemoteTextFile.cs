using System.Net;

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
    public static async Task<RemoteText> GetAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
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

            return new RemoteText(
                RemoteTextOutcome.Fetched,
                await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false),
                "",
                response.Headers.ETag?.ToString());
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
}
