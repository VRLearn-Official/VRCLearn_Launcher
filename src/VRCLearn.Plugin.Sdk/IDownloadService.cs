namespace VRCLearn.Plugin.Sdk;

public interface IDownloadService
{
    Task DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed record DownloadRequest(Uri Url, string FilePath, string? Sha256 = null);

public sealed record DownloadProgress(long Received, long? Total);
