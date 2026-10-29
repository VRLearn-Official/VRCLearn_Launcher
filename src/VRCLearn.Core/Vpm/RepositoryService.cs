using System.Text;
using System.Text.Json;
using VRCLearn.Core.Http;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Repositories;

namespace VRCLearn.Core.Vpm;

public sealed class RepositoryService
{
    private readonly ILauncherSettingsStore _settingsStore;
    private readonly IRepositoryCacheStore _cacheStore;
    private readonly IVpmHttpClient _http;

    public RepositoryService(
        ILauncherSettingsStore settingsStore,
        IRepositoryCacheStore cacheStore,
        IVpmHttpClient http)
    {
        _settingsStore = settingsStore;
        _cacheStore = cacheStore;
        _http = http;
    }

    private LauncherSettings Settings => _settingsStore.Current;

    public IReadOnlyList<RepositoryEntry> List() =>
        Settings.UserRepos
            .Select((repo, index) => new RepositoryEntry(index, repo.Id, repo.Url, repo.Name, repo.LocalPath))
            .ToList();

    /// <summary>缓存缺失的条目 <c>Cached</c> 为 null。</summary>
    public IReadOnlyList<(RepositoryEntry Entry, LocalCachedRepository? Cached)> ListWithCached() =>
        Settings.UserRepos
            .Select((repo, index) => (
                new RepositoryEntry(index, repo.Id, repo.Url, repo.Name, repo.LocalPath),
                _cacheStore.Load(repo.LocalPath)))
            .ToList();

    public IReadOnlyList<LocalCachedRepository> CachedRepositories() =>
        [.. Settings.UserRepos
            .Select(x => _cacheStore.Load(x.LocalPath))
            .OfType<LocalCachedRepository>()];

    public async Task<AddRepositoryOutcome> AddAsync(
        Uri url,
        string? name,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        RemoteRepository repository;
        string? etag;

        if (url.Scheme is not ("http" or "https"))
        {
            return new AddRepositoryOutcome(AddRepositoryStatus.Unreachable, url.Scheme);
        }

        try
        {
            (repository, etag) = await RemoteRepository
                .DownloadAsync(_http, url, headers, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return new AddRepositoryOutcome(AddRepositoryStatus.Unreachable, exception.Message);
        }
        catch (Exception exception) when (exception is RepositoryParseException or JsonException)
        {
            return new AddRepositoryOutcome(AddRepositoryStatus.InvalidContent, exception.Message);
        }

        if (Settings.ContainsRepo(url, repository.Id))
        {
            return new AddRepositoryOutcome(AddRepositoryStatus.AlreadyAdded);
        }

        var relativePath = RepositoryCachePath.For(repository.Id, url);
        var cached = new LocalCachedRepository(repository, headers);
        cached.SetETag(etag);
        _cacheStore.Save(relativePath, cached);

        Settings.UserRepos.Add(UserRepoSetting.Create(
            _cacheStore.Resolve(relativePath),
            name ?? repository.Name,
            url,
            repository.Id));
        _settingsStore.Save();

        return new AddRepositoryOutcome(AddRepositoryStatus.Success);
    }

    public RemoveRepositoryStatus Remove(int index, string? expectedId)
    {
        if (index < 0 || index >= Settings.UserRepos.Count)
        {
            return RemoveRepositoryStatus.IndexOutOfRange;
        }

        var repository = Settings.UserRepos[index];
        if (expectedId is not null && repository.Id != expectedId)
        {
            return RemoveRepositoryStatus.Stale;
        }

        Settings.UserRepos.RemoveAt(index);
        _settingsStore.Save();
        _cacheStore.Delete(repository.LocalPath);

        return RemoveRepositoryStatus.Success;
    }

    /// <summary>下标都是挪之前那份列表里的位置，<paramref name="insertIndex"/> 等于条数表示挪到末尾。</summary>
    public ReorderRepositoryStatus Reorder(int from, int insertIndex, IReadOnlyList<string?> expectedIds)
    {
        var repositories = Settings.UserRepos;

        if (from < 0
            || from >= repositories.Count
            || insertIndex < 0
            || insertIndex > repositories.Count
            || !repositories.Select(x => x.Id).SequenceEqual(expectedIds))
        {
            return ReorderRepositoryStatus.Stale;
        }

        var to = RepositoryMove.Target(from, insertIndex);

        if (to == from)
        {
            return ReorderRepositoryStatus.Success;
        }

        var moved = repositories[from];
        repositories.RemoveAt(from);
        repositories.Insert(to, moved);
        _settingsStore.Save();

        return ReorderRepositoryStatus.Success;
    }

    public string Export() => RepositoriesFileWriter.Write(Settings.UserRepos);

    public async Task<ImportFromUrlOutcome> ImportFromUrlAsync(Uri url, CancellationToken cancellationToken)
    {
        string content;

        try
        {
            await using var stream = await _http.GetAsync(url, NoHeaders, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return new ImportFromUrlOutcome(null, exception.Message);
        }

        return new ImportFromUrlOutcome(await ImportAsync(content, cancellationToken).ConfigureAwait(false));
    }

    public void ClearPackageCache() => _cacheStore.ClearPackageCache();

    public long PackageCacheSize() => _cacheStore.PackageCacheSize();

    public PackageCacheMigrationResult MigratePackageCache(
        string destinationDirectory,
        IProgress<FileProgress>? progress = null) =>
        _cacheStore.MigratePackageCache(destinationDirectory, progress);

    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    public async Task<RefreshSummary> RefreshAsync(CancellationToken cancellationToken)
    {
        var updated = 0;
        var notModified = 0;
        var failed = 0;

        foreach (var setting in Settings.UserRepos)
        {
            if (setting.Url is not { } url)
            {
                continue;
            }

            var cached = _cacheStore.Load(setting.LocalPath);

            try
            {
                var result = await RemoteRepository
                    .DownloadWithETagAsync(_http, url, setting.Headers, cached?.ETag, cancellationToken)
                    .ConfigureAwait(false);

                if (result is null)
                {
                    notModified++;
                    continue;
                }

                if (cached is null)
                {
                    cached = new LocalCachedRepository(result.Value.Repository, setting.Headers);
                }
                else
                {
                    cached.SetRepository(result.Value.Repository);
                }

                cached.SetETag(result.Value.ETag);
                _cacheStore.Save(setting.LocalPath, cached);
                updated++;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or RepositoryParseException or JsonException)
            {
                failed++;
            }
        }

        return new RefreshSummary(updated, notModified, failed);
    }

    public async Task<ImportSummary> ImportAsync(string content, CancellationToken cancellationToken)
    {
        var parsed = RepositoriesFileParser.Parse(content);
        var added = 0;
        var alreadyAdded = 0;
        var failed = 0;

        foreach (var repository in parsed.Repositories)
        {
            var outcome = await AddAsync(repository.Url, null, repository.Headers, cancellationToken).ConfigureAwait(false);

            switch (outcome.Status)
            {
                case AddRepositoryStatus.Success:
                    added++;
                    break;
                case AddRepositoryStatus.AlreadyAdded:
                    alreadyAdded++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return new ImportSummary(added, alreadyAdded, failed, parsed.UnparseableLines.Count);
    }
}
