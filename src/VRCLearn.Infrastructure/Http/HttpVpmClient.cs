using System.Net;
using System.Net.Http.Headers;
using VRCLearn.Core.Http;

namespace VRCLearn.Infrastructure.Http;

public sealed class HttpVpmClient : IVpmHttpClient
{
    private readonly HttpClient _client;

    public HttpVpmClient(HttpClient client) => _client = client;

    private static readonly ProductInfoHeaderValue UserAgent = new(
        "VRCLearnLauncher",
        typeof(HttpVpmClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    public static HttpClient CreateHttpClient(MutableProxy proxy)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            Proxy = proxy,
        });

        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        return client;
    }

    public async Task<Stream> GetAsync(
        Uri url,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        using var request = HttpRequests.Get(url, headers);

        var response = await HttpRequests
            .SendAsync(_client, request, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<HttpFetchResult?> GetWithETagAsync(
        Uri url,
        IReadOnlyDictionary<string, string> headers,
        string? currentETag,
        CancellationToken cancellationToken)
    {
        using var request = HttpRequests.Get(url, headers);

        if (currentETag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", currentETag);
        }

        var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (currentETag is not null && response.StatusCode == HttpStatusCode.NotModified)
        {
            response.Dispose();
            return null;
        }

        try
        {
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new HttpFetchResult(stream, ReadETag(response));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static string? ReadETag(HttpResponseMessage response) =>
        response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null;
}
