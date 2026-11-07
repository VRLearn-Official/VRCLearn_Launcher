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

internal sealed record LauncherReleaseAsset(Uri Url, string Sha256, long Size);

public sealed class UpdateService(HttpClient http, UpdateStateStore states, ILogger<UpdateService> logger)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private const string DefaultReleaseApiUrl =
        "https://api.github.com/repos/VRLearn-Official/VRCLearn_Launcher/releases/latest";

    public const string DefaultDownloadUrl = "https://start.gamemode1.com/pages/vrclearnlauncher/";

    private const string OfficialReleasePage = "https://github.com/VRLearn-Official/VRCLearn_Launcher/releases";

    private static readonly IReadOnlyDictionary<string, string> ApiHeaders =
        new Dictionary<string, string>
        {
            ["Accept"] = "application/vnd.github+json",
            ["X-GitHub-Api-Version"] = "2026-03-10",
        };

    public string CurrentVersionText => CurrentVersion.ToString();

    internal Version CurrentVersion { get; } =
        Version.Parse(typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    internal bool HasCustomSource =>
        Environment.GetEnvironmentVariable("VRCL_VERSION_URL") is { Length: > 0 }
        || Environment.GetEnvironmentVariable("VRCL_DOWNLOAD_URL") is { Length: > 0 };

    public string ManualDownloadUrl => DownloadUrl();

    internal async Task<LauncherReleaseAsset> GetInstallerAsync(string version, CancellationToken cancellationToken)
    {
        if (HasCustomSource || ParseVersion(version) is not { } target || target <= CurrentVersion
            || target.ToString() != version)
        {
            throw new InvalidDataException("The requested version is not eligible for automatic installation.");
        }

        var metadata = await RemoteTextFile.GetAsync(
            http, DefaultReleaseApiUrl, cancellationToken, ApiHeaders, maxBytes: 512 * 1024).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (metadata.Outcome != RemoteTextOutcome.Fetched)
        {
            throw new IOException($"The official release could not be read: {metadata.Detail}");
        }

        var release = JsonSerializer.Deserialize<GitHubInstallerRelease>(metadata.Text.TrimStart('\uFEFF'));
        var tag = $"v{version}";

        if (release is not { Draft: false, Prerelease: false, Assets: { } assets } || release.TagName != tag)
        {
            throw new InvalidDataException("The latest official stable release changed. Check for updates again.");
        }

        var name = $"VRCLearnLauncher-{version}-win-x64-Setup.exe";
        var installer = FindAsset(assets, tag, name);
        var checksum = FindAsset(assets, tag, name + ".sha256");

        if (installer.Size is <= 0 or > UpdateInstallation.MaxInstallerSize)
        {
            throw new InvalidDataException("The official installer size is invalid.");
        }

        var remote = await RemoteTextFile.GetAsync(
            http, checksum.Url!, cancellationToken, maxBytes: 512).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var parts = remote.Text.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        if (remote.Outcome != RemoteTextOutcome.Fetched || parts is not [var hash, var file]
            || file != name || !UpdateInstallation.IsHash(hash)
            || (installer.Digest is { Length: > 0 } digest
                && !digest.Equals($"sha256:{hash}", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The official checksum does not match the installer.");
        }

        return new LauncherReleaseAsset(new Uri(installer.Url!), hash, installer.Size);
    }

    private static GitHubAsset FindAsset(IEnumerable<GitHubAsset> assets, string tag, string name)
    {
        var matches = assets.Where(asset => asset.Name == name).ToArray();

        if (matches is not [var asset] || asset.Url != $"{OfficialReleasePage}/download/{tag}/{name}")
        {
            throw new InvalidDataException($"The official release does not contain {name}.");
        }

        return asset;
    }

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

        var current = CurrentVersion;

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

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }
    }

    private sealed class GitHubInstallerRelease : GitHubRelease
    {
        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? Url { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
