using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VRCLearn.Infrastructure.Http;

namespace VRCLearn.Infrastructure.Updates;

public enum UpdateKind
{
    UpToDate,

    UpdateAvailable,

    Failed,
}

public sealed record UpdateStatus(UpdateKind Kind, string? Version = null, string? DownloadUrl = null);

public sealed class UpdateService(HttpClient http, UpdateStateStore states, ILogger<UpdateService> logger)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private const string DefaultReleaseApiUrl =
        "https://api.github.com/repos/VRLearn-Official/VRCLearn_Launcher/releases/latest";

    private const string DefaultDownloadUrl = "https://start.gamemode1.com/pages/vrclearnlauncher/";

    private static readonly IReadOnlyDictionary<string, string> ApiHeaders =
        new Dictionary<string, string>
        {
            ["Accept"] = "application/vnd.github+json",
            ["X-GitHub-Api-Version"] = "2026-03-10",
        };

    public string CurrentVersionText =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<UpdateStatus> CheckAsync(bool force = false)
    {
        var state = states.Load();

        if (!force && DateTimeOffset.UtcNow - state.CheckedAt < CheckInterval)
        {
            if (state.Tag.Length > 0)
            {
                return Evaluate(state.Tag);
            }

            logger.LogInformation("上次检查失败，本次启动不再重试");

            return new UpdateStatus(UpdateKind.Failed);
        }

        var remote = await RemoteTextFile.GetAsync(
            http, VersionUrl(), CancellationToken.None, HeadersFor(state.ETag));

        if (remote.Outcome == RemoteTextOutcome.NotModified && state.Tag.Length > 0)
        {
            state.CheckedAt = DateTimeOffset.UtcNow;
            state.ETag = remote.ETag ?? state.ETag;
            states.Save(state);

            return Evaluate(state.Tag);
        }

        if (remote.Outcome != RemoteTextOutcome.Fetched)
        {
            return Fail(state, $"获取最新版本号失败：{remote.Detail}");
        }

        if (ReadTag(remote.Text) is not { } text)
        {
            return Fail(state, "版本接口返回的内容无法解析");
        }

        state.Tag = text;
        state.ETag = remote.ETag ?? state.ETag;
        state.CheckedAt = DateTimeOffset.UtcNow;
        states.Save(state);

        return Evaluate(text);
    }

    private UpdateStatus Fail(UpdateState state, string message)
    {
        logger.LogWarning("{Message}", message);

        state.CheckedAt = DateTimeOffset.UtcNow;
        states.Save(state);

        return new UpdateStatus(UpdateKind.Failed);
    }

    private UpdateStatus Evaluate(string tag)
    {
        if (ParseVersion(tag) is not { } latest)
        {
            logger.LogWarning("版本号格式不对：{Text}", tag);

            return new UpdateStatus(UpdateKind.Failed);
        }

        var current = Version.TryParse(CurrentVersionText, out var local) ? local : new Version(0, 0);

        if (latest > current)
        {
            logger.LogInformation("发现新版本：{Latest}，当前 {Current}", latest, current);

            return new UpdateStatus(UpdateKind.UpdateAvailable, latest.ToString(), DownloadUrl());
        }

        logger.LogInformation("已是最新版本：{Current}", current);

        return new UpdateStatus(UpdateKind.UpToDate);
    }

    private static IReadOnlyDictionary<string, string> HeadersFor(string etag)
    {
        if (etag.Length == 0)
        {
            return ApiHeaders;
        }

        var headers = new Dictionary<string, string>(ApiHeaders) { ["If-None-Match"] = etag };

        return headers;
    }

    private static string? ReadTag(string json)
    {
        try
        {
            var release = JsonSerializer.Deserialize<GitHubRelease>(json.TrimStart('\uFEFF'));

            return NormalizeTag(release?.TagName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeTag(string? tag) =>
        (tag ?? "").Trim().TrimStart('\uFEFF').Trim().TrimStart('v', 'V').Trim();

    private static Version? ParseVersion(string text) =>
        text.Split('.') is { Length: 3 } parts
        && parts.All(x => int.TryParse(x, out _))
        && Version.TryParse(text, out var version)
            ? version
            : null;

    private static string VersionUrl() =>
        Environment.GetEnvironmentVariable("VRCL_VERSION_URL") is { Length: > 0 } custom
            ? custom
            : DefaultReleaseApiUrl;

    private static string DownloadUrl() =>
        Environment.GetEnvironmentVariable("VRCL_DOWNLOAD_URL") is { Length: > 0 } custom
            ? custom
            : DefaultDownloadUrl;

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }
    }
}
