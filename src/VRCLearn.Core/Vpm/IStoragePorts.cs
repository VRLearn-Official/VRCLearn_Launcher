using VRCLearn.Core.Projects;
using VRCLearn.Core.Repositories;

namespace VRCLearn.Core.Vpm;

public interface ILauncherSettingsStore
{
    event EventHandler? Changed;

    LauncherSettings Current { get; }

    void Reload();

    void Save();
}

public interface IRepositoryCacheStore
{
    /// <summary>设置文件里存的是绝对路径。</summary>
    string Resolve(string relativePath);

    LocalCachedRepository? Load(string relativePath);

    void Save(string relativePath, LocalCachedRepository repository);

    void Delete(string relativePath);

    /// <summary>只清下载的包压缩包，仓库清单的缓存留着。</summary>
    void ClearPackageCache();

    /// <summary>上面那些包压缩包一共占了多少字节。</summary>
    long PackageCacheSize();

    /// <summary>把包压缩包搬到新目录，仓库清单的缓存不动。</summary>
    PackageCacheMigrationResult MigratePackageCache(
        string destinationDirectory,
        IProgress<FileProgress>? progress = null);
}
