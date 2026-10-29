using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VRCLearn.Infrastructure.Http;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

public sealed class HostDownloadService : IDownloadService, IDisposable
{
    private const int BufferSize = 81920;
    private const long ReportInterval = 256 * 1024;
    private const int ParallelConnections = 2;
    private const long SegmentSize = 16 * 1024 * 1024;
    private const int MaxAttempts = 4;

    private readonly HttpClient _http;
    private readonly ILogger<HostDownloadService> _logger;
    private long _resumeAt;

    public HostDownloadService(MutableProxy proxy, ILogger<HostDownloadService> logger)
    {
        _logger = logger;
        _http = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.All,
            Proxy = proxy,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("VRCLearn");
    }

    public async Task DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (request.Url is null || request.Url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("download url must use https", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new ArgumentException("file path is required", nameof(request));
        }

        var target = Path.GetFullPath(request.FilePath);
        var directory = Path.GetDirectoryName(target)!;
        var temporary = target + ".part";

        Directory.CreateDirectory(directory);
        TryDelete(temporary);

        try
        {
            if (!await TryDownloadParallelAsync(request.Url, temporary, progress, cancellationToken)
                    .ConfigureAwait(false))
            {
                await DownloadSingleAsync(request.Url, temporary, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(request.Sha256))
            {
                var expected = NormalizeHash(request.Sha256);
                var actual = await ComputeSha256Async(temporary, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"sha256 mismatch: expected {expected}, got {actual}");
                }
            }

            File.Move(temporary, target, overwrite: true);
            _logger.LogInformation("已下载 {Url} -> {Path}", request.Url, target);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "下载失败：{Url}", request.Url);
            TryDelete(temporary);
            throw;
        }
    }

    private async Task<bool> TryDownloadParallelAsync(
        Uri url,
        string path,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        long length;

        try
        {
            using var probe = await SendRangeAsync(url, 0, 0, cancellationToken).ConfigureAwait(false);

            if (probe.StatusCode != HttpStatusCode.PartialContent
                || probe.Content.Headers.ContentRange?.Length is not { } total
                || total <= SegmentSize)
            {
                return false;
            }

            length = total;
        }
        catch (HttpRequestException)
        {
            return false;
        }

        using var handle = File.OpenHandle(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileOptions.Asynchronous,
            length);

        long received = 0;
        long reported = 0;
        var segments = (int)((length + SegmentSize - 1) / SegmentSize);

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, segments),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = ParallelConnections,
                    CancellationToken = cancellationToken,
                },
                async (index, token) =>
                {
                    var start = index * SegmentSize;
                    var end = Math.Min(start + SegmentSize, length) - 1;

                    using var response = await SendRangeAsync(url, start, end, token).ConfigureAwait(false);

                    if (response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new InvalidDataException("server did not honor the range request");
                    }

                    await using var stream = await response.Content
                        .ReadAsStreamAsync(token)
                        .ConfigureAwait(false);

                    var buffer = new byte[BufferSize];
                    var offset = start;

                    while (true)
                    {
                        var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);

                        if (read == 0)
                        {
                            break;
                        }

                        await RandomAccess
                            .WriteAsync(handle, buffer.AsMemory(0, read), offset, token)
                            .ConfigureAwait(false);

                        offset += read;

                        var current = Interlocked.Add(ref received, read);
                        var last = Interlocked.Read(ref reported);

                        if (current - last >= ReportInterval || current == length)
                        {
                            Interlocked.CompareExchange(ref reported, current, last);
                            progress?.Report(new DownloadProgress(current, length));
                        }
                    }
                }).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }

        if (Interlocked.Read(ref received) != length)
        {
            throw new InvalidDataException("incomplete download");
        }

        return true;
    }

    private Task<HttpResponseMessage> SendRangeAsync(
        Uri url,
        long from,
        long to,
        CancellationToken cancellationToken) =>
        SendAsync(url, new RangeHeaderValue(from, to), cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        Uri url,
        RangeHeaderValue? range,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await WaitForServerAsync(cancellationToken).ConfigureAwait(false);

            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Range = range;

            var response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (IsThrottled(response.StatusCode) && attempt < MaxAttempts)
            {
                var delay = RetryDelay(response, attempt);

                _logger.LogWarning(
                    "服务器返回 {Status}，暂停 {Delay} 后重试：{Url}",
                    (int)response.StatusCode,
                    delay,
                    url);

                response.Dispose();
                PauseAll(delay);
                continue;
            }

            try
            {
                response.EnsureSuccessStatusCode();

                if (response.RequestMessage?.RequestUri is { } finalUri
                    && finalUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException("download was redirected to a non-https url");
                }

                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    private static bool IsThrottled(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var after = response.Headers.RetryAfter;
        var delay = after?.Delta;

        if (delay is null && after?.Date is { } date)
        {
            delay = date - DateTimeOffset.UtcNow;
        }

        var backoff = TimeSpan.FromSeconds(2 * attempt);
        var wait = delay is { } value && value > TimeSpan.Zero ? value : backoff;

        return wait + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
    }

    private void PauseAll(TimeSpan delay)
    {
        var until = DateTime.UtcNow.Add(delay).Ticks;
        var current = Interlocked.Read(ref _resumeAt);

        while (until > current
            && Interlocked.CompareExchange(ref _resumeAt, until, current) != current)
        {
            current = Interlocked.Read(ref _resumeAt);
        }
    }

    private async Task WaitForServerAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var wait = new DateTime(Interlocked.Read(ref _resumeAt), DateTimeKind.Utc)
                - DateTime.UtcNow;

            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DownloadSingleAsync(
        Uri url,
        string path,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(url, null, cancellationToken).ConfigureAwait(false);

        var total = response.Content.Headers.ContentLength;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        long received = 0;
        long reported = 0;

        await using (var destination = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true))
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                await destination
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);

                received += read;

                if (received - reported >= ReportInterval
                    || (total is { } expected && received == expected))
                {
                    reported = received;
                    progress?.Report(new DownloadProgress(received, total));
                }
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new DownloadProgress(received, total ?? received));
    }

    public void Dispose() => _http.Dispose();

    private static string NormalizeHash(string hash) =>
        hash.Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
