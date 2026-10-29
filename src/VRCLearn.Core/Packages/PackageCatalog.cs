using VRCLearn.Core.Repositories;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Packages;

/// <summary>按名字归并，同一个「包@版本」保留先遇到的。</summary>
public sealed class PackageCatalog
{
    private readonly Dictionary<string, List<PackageInfo>> _byName = [];
    private readonly HashSet<(string Name, PackageVersion Version)> _seen = [];

    private IReadOnlyList<PackageInfo>? _all;

    public static PackageCatalog FromRepositories(
        IEnumerable<LocalCachedRepository> repositories,
        IEnumerable<PackageInfo>? userPackages = null)
    {
        var catalog = new PackageCatalog();

        foreach (var package in userPackages ?? [])
        {
            catalog.Add(package);
        }

        foreach (var repository in repositories)
        {
            foreach (var packages in repository.GetPackages())
            {
                foreach (var manifest in packages.AllVersions)
                {
                    catalog.Add(PackageInfo.Remote(manifest, repository));
                }
            }
        }

        return catalog;
    }

    public IReadOnlyList<PackageInfo> All => _all ??= [.. _byName.Values.SelectMany(x => x)];

    public IEnumerable<string> PackageNames => _byName.Keys;

    public bool HasName(string name) => _byName.Keys.Contains(name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PackageInfo> GetVersions(string name) =>
        _byName.TryGetValue(name, out var versions) ? versions : [];

    public PackageInfo? Find(string name, PackageVersion version) =>
        GetVersions(name).FirstOrDefault(x => x.Version == version);

    public PackageInfo? Find(string name, VersionSelector selector) =>
        GetVersions(name).Where(x => selector.Satisfies(x.Manifest)).MaxBy(x => x.Version);

    /// <summary>比 <paramref name="installed"/> 新、且这个项目用得上的最高版本；没有返回 null。</summary>
    public PackageInfo? NewerThan(
        string name,
        PackageVersion installed,
        UnityVersion? unityVersion,
        bool allowPrerelease) =>
        Find(name, VersionSelector.LatestFor(unityVersion, allowPrerelease)) is { } latest
        && latest.Version > installed
            ? latest
            : null;

    public PackageInfo? GetLatest(string name) =>
        GetVersions(name)
            .Where(x => !x.Manifest.IsYanked)
            .MaxBy(x => x.Version);

    private void Add(PackageInfo package)
    {
        if (!_seen.Add((package.Name, package.Version)))
        {
            return;
        }

        if (!_byName.TryGetValue(package.Name, out var versions))
        {
            _byName[package.Name] = versions = [];
        }

        versions.Add(package);
    }
}
