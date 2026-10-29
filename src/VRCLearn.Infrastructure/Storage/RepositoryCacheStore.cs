using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Repositories;
using VRCLearn.Core.Vpm;

namespace VRCLearn.Infrastructure.Storage;

public sealed class RepositoryCacheStore(AppPaths paths, GuiConfigStore configs, ILogger<RepositoryCacheStore> logger)
    : IRepositoryCacheStore
{
    /// <summary>仓库清单是几 MB 的 JSON，解析一次就一直用，写盘和删除时才换。</summary>
    private readonly Dictionary<string, LocalCachedRepository?> _parsed = new(StringComparer.OrdinalIgnoreCase);

    private string PackageCacheDirectory => PackageCachePath.Effective(configs.Current, paths);

    public LocalCachedRepository? Load(string relativePath)
    {
        if (_parsed.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var path = Resolve(relativePath);

        try
        {
            cached = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject json
                ? LocalCachedRepository.FromJson(json)
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or RepositoryParseException)
        {
            cached = null;
        }

        _parsed[relativePath] = cached;

        return cached;
    }

    public void Save(string relativePath, LocalCachedRepository repository)
    {
        JsonFileStore.Save(
            Resolve(relativePath),
            repository.ToJson(),
            message => logger.LogWarning("{Message}", message));

        _parsed[relativePath] = repository;
    }

    public void Delete(string relativePath)
    {
        var path = Resolve(relativePath);

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "删除仓库缓存失败：{Path}", path);
        }

        _parsed.Remove(relativePath);
    }

    public void ClearPackageCache()
    {
        var root = PackageCacheDirectory;

        if (!PackageCachePath.IsSafeDestination(paths, root) || !Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "vrc-get-*.zip*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }

            DeleteEmptyDirectories(directory);
        }
    }

    private static void DeleteEmptyDirectories(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            DeleteEmptyDirectories(child);
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    public long PackageCacheSize()
    {
        var root = PackageCacheDirectory;

        if (!Directory.Exists(root))
        {
            return 0;
        }

        return Directory
            .EnumerateDirectories(root)
            .SelectMany(x => Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories))
            .Sum(x => new FileInfo(x).Length);
    }

    public PackageCacheMigrationResult MigratePackageCache(
        string destinationDirectory,
        IProgress<FileProgress>? progress = null) =>
        PackageCacheMigrator.Migrate(paths, PackageCacheDirectory, destinationDirectory, progress);

    /// 入参可能是绝对路径，原样使用。
    public string Resolve(string relativePath) =>
        Path.Combine(paths.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
