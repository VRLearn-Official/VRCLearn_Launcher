using System.Net.Http;
using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Plugins;
using VRCLearn.Core.Plugins.Hub;
using VRCLearn.Core.Versioning;
using VRCLearn.Infrastructure.Http;

namespace VRCLearn.Infrastructure.Plugins;

public sealed record PluginHubCheckResult(
    IReadOnlyList<PluginHubUpdateCandidate> Updates,
    IReadOnlyList<string> Failures);

public sealed class PluginHubService(
    HttpClient http,
    AppPaths paths,
    PluginHubStateStore states,
    ILogger<PluginHubService> logger)
{
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromHours(24);

    public async Task<PluginHubCheckResult> CheckAsync(
        IReadOnlyList<PluginHubTarget> installed,
        bool force,
        CancellationToken cancellationToken)
    {
        var updates = new List<PluginHubUpdateCandidate>();
        var failures = new List<string>();
        var groups = new Dictionary<string, List<PluginHubTarget>>(StringComparer.Ordinal);

        CleanupStaleDownloads();

        foreach (var target in installed)
        {
            if (!PluginHubEndpoint.TryNormalize(target.Hub, out var hub))
            {
                failures.Add($"{target.PluginId}: invalid hub");
                continue;
            }

            if (!groups.TryGetValue(hub, out var group))
            {
                groups[hub] = group = [];
            }

            group.Add(target with { Hub = hub });
        }

        if (groups.Count == 0)
        {
            return new PluginHubCheckResult(updates, failures);
        }

        var state = states.Load();
        var now = DateTimeOffset.UtcNow;

        foreach (var (hub, targets) in groups)
        {
            var entry = state.Hubs.FirstOrDefault(x => string.Equals(x.Hub, hub, StringComparison.Ordinal));

            if (force || entry is null || now - entry.CheckedAt >= CheckInterval)
            {
                entry = await RefreshAsync(hub, entry, state, now, cancellationToken).ConfigureAwait(false);
            }

            if (entry.Failed)
            {
                failures.Add($"{hub}: {entry.Error}");
            }

            foreach (var target in targets)
            {
                if (CachedEntry(entry, target.PluginId) is not { } cached)
                {
                    continue;
                }

                if (PluginHubUpdateChecker.Find(target, cached, PluginHostInfo.Version, PluginHostInfo.ApiVersion)
                    is { } candidate)
                {
                    updates.Add(candidate);
                }
            }
        }

        states.Save(state);

        return new PluginHubCheckResult(updates, failures);
    }

    private async Task<PluginHubStateEntry> RefreshAsync(
        string hub,
        PluginHubStateEntry? existing,
        PluginHubState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entry = existing ?? new PluginHubStateEntry { Hub = hub };

        if (existing is null)
        {
            state.Hubs.Add(entry);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (entry.ETag.Length > 0)
        {
            headers["If-None-Match"] = entry.ETag;
        }

        var result = await RemoteTextFile
            .GetAsync(http, PluginHubEndpoint.IndexUri(hub).AbsoluteUri, cancellationToken, headers, PluginHubIndexParser.MaxChars)
            .ConfigureAwait(false);

        entry.CheckedAt = now;

        if (result.Outcome == RemoteTextOutcome.Fetched)
        {
            if (PluginHubIndexParser.TryParse(result.Text, out var index, out var error))
            {
                entry.Failed = false;
                entry.Error = "";
                entry.ETag = result.ETag ?? entry.ETag;
                entry.Releases = [.. index!.Plugins.SelectMany(ToCached)];
            }
            else
            {
                entry.Failed = true;
                entry.Error = error;
                logger.LogWarning("插件市场索引无法解析：{Hub}：{Error}", hub, error);
            }
        }
        else if (result.Outcome == RemoteTextOutcome.NotModified)
        {
            entry.Failed = false;
            entry.Error = "";
            entry.ETag = result.ETag ?? entry.ETag;
        }
        else
        {
            entry.Failed = true;
            entry.Error = result.Detail;
            logger.LogWarning("插件市场索引获取失败：{Hub}：{Detail}", hub, result.Detail);
        }

        return entry;
    }

    private static IEnumerable<PluginHubCachedRelease> ToCached(PluginHubEntry entry)
    {
        if (entry.SelectLatestRelease() is not { } release)
        {
            yield break;
        }

        yield return new PluginHubCachedRelease
        {
            Id = entry.Id,
            Version = release.Version.ToString(),
            Url = release.Url,
            Sha256 = release.Sha256 ?? "",
            Size = release.Size,
            ApiVersion = entry.ApiVersion,
            MinHostVersion = entry.MinHostVersion?.ToString() ?? "",
        };
    }

    private static PluginHubEntry? CachedEntry(PluginHubStateEntry state, string id)
    {
        if (state.Releases.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal)) is not { } release
            || !PackageVersion.TryParse(release.Version, out var version))
        {
            return null;
        }

        PackageVersion? minimum = null;

        if (release.MinHostVersion.Length > 0 && PackageVersion.TryParse(release.MinHostVersion, out var parsed))
        {
            minimum = parsed;
        }

        return new PluginHubEntry
        {
            Id = release.Id,
            ApiVersion = release.ApiVersion,
            MinHostVersion = minimum,
            Versions =
            [
                new PluginHubRelease
                {
                    Version = version,
                    Size = release.Size,
                    Url = release.Url,
                    Sha256 = release.Sha256.Length > 0 ? release.Sha256 : null,
                },
            ],
        };
    }

    private void CleanupStaleDownloads()
    {
        try
        {
            if (!Directory.Exists(paths.PluginUpdateTempDirectory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-1);

            foreach (var directory in Directory.EnumerateDirectories(paths.PluginUpdateTempDirectory))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(directory) < cutoff)
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "清理插件更新临时目录失败：{Directory}", directory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "扫描插件更新临时目录失败：{Directory}", paths.PluginUpdateTempDirectory);
        }
    }
}
