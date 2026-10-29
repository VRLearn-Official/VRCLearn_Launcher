using VRCLearn.Core.Repositories;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Packages;

/// <summary>待安装的一个包，来源为远端仓库缓存或本地目录。</summary>
public sealed class PackageInfo
{
    private PackageInfo(PackageManifest manifest, LocalCachedRepository? repository, string? localPath)
    {
        Manifest = manifest;
        Repository = repository;
        LocalPath = localPath;
    }

    public PackageManifest Manifest { get; }

    public LocalCachedRepository? Repository { get; }

    public string? LocalPath { get; }

    public string Name => Manifest.Name;

    public PackageVersion Version => Manifest.Version;

    public IReadOnlyDictionary<string, VersionRange> VpmDependencies => Manifest.VpmDependencies;

    public static PackageInfo Remote(PackageManifest manifest, LocalCachedRepository repository) =>
        new(manifest, repository, null);

    public static PackageInfo Local(PackageManifest manifest, string localPath) =>
        new(manifest, null, localPath);

    public override string ToString() => $"{Name}@{Version}";
}
