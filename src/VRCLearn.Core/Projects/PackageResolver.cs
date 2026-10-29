using VRCLearn.Core.Packages;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

public sealed record PackageResolution(
    IReadOnlyList<PackageInfo> NewPackages,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Conflicts,
    IReadOnlyList<string> FoundLegacyPackages);

/// <summary>同一个包被多处要求时取范围交集。</summary>
public sealed class MissingDependencies
{
    private readonly OrderedDictionary<string, VersionRange> _ranges = [];

    public bool IsEmpty => _ranges.Count == 0;

    public IReadOnlyList<MissingDependency> Entries =>
        [.. _ranges.Select(x => new MissingDependency(x.Key, x.Value))];

    public void Add(string name, VersionRange range) =>
        _ranges[name] = _ranges.TryGetValue(name, out var existing) ? range.Intersect(existing) : range;
}

internal sealed class PackageResolver
{
    /// <summary>项目根（<c>dependencies</c> 段）在要求表里的键。</summary>
    private const string Root = "";

    private readonly PackageCatalog _catalog;
    private readonly UnityVersion? _unityVersion;
    private readonly bool _allowPrerelease;
    private readonly PackageQueue _queue;
    private readonly OrderedDictionary<string, DependencyInfo> _dependencies = [];
    private readonly HashSet<string> _unlockedNames = [];

    private PackageResolver(
        PackageCatalog catalog,
        UnityVersion? unityVersion,
        IReadOnlyList<PackageInfo> packages,
        bool allowPrerelease)
    {
        _catalog = catalog;
        _unityVersion = unityVersion;
        _allowPrerelease = allowPrerelease;
        _queue = new PackageQueue(packages);

        foreach (var package in packages)
        {
            Info(package.Name).AllowPrerelease = true;
        }

        _dependencies[Root] = new DependencyInfo();
    }

    public static PackageResolution Collect(
        PackageCatalog catalog,
        IEnumerable<KeyValuePair<string, DependencyRange>> dependencies,
        IEnumerable<LockedDependencyInfo> lockedDependencies,
        IEnumerable<(string Name, PackageManifest? Manifest)> unlockedPackages,
        Func<string, LockedDependencyInfo?> getLocked,
        UnityVersion? unityVersion,
        IReadOnlyList<PackageInfo> packages,
        bool allowPrerelease,
        MissingDependencies missingDependencies)
    {
        var resolver = new PackageResolver(catalog, unityVersion, packages, allowPrerelease);

        resolver.AddRootDependencies(dependencies, getLocked);

        foreach (var locked in lockedDependencies)
        {
            resolver.AddLockedDependency(locked);
        }

        foreach (var (name, manifest) in unlockedPackages)
        {
            resolver.AddUnlockedName(name);

            if (manifest is not null)
            {
                resolver.AddUnlockedName(manifest.Name);
                resolver.AddUnlockedManifest(manifest);
            }
        }

        resolver.Run(missingDependencies);

        return resolver.BuildResult();
    }

    private void AddRootDependencies(
        IEnumerable<KeyValuePair<string, DependencyRange>> dependencies,
        Func<string, LockedDependencyInfo?> getLocked)
    {
        foreach (var (name, dependency) in dependencies)
        {
            VersionRange range;
            bool allowPre;

            if (dependency.AsSingleVersion() is { } minimum)
            {
                allowPre = minimum.IsPre;

                if (getLocked(name) is { } locked)
                {
                    allowPre |= locked.Version.IsPre;

                    if (locked.Version < minimum)
                    {
                        minimum = locked.Version;
                    }
                }

                range = VersionRange.SameOrLater(minimum);
            }
            else
            {
                range = dependency.AsRange();
                allowPre = range.ContainsPre;
            }

            Info(Root).Requirements[name] = range;

            var info = Info(name);
            info.Requirements[Root] = range;
            info.AllowPrerelease |= allowPre;
        }
    }

    private void AddLockedDependency(LockedDependencyInfo locked)
    {
        var info = Info(locked.Name);
        info.SetUsingInfo(locked.Version, locked.Dependencies.Keys);

        if (_catalog.Find(locked.Name, VersionSelector.ForSpecificVersion(locked.Version)) is { } package)
        {
            info.Supersedes = [.. package.Manifest.LegacyPackages];

            foreach (var legacy in package.Manifest.LegacyPackages)
            {
                Info(legacy).SupersededBy.Add(locked.Name);
            }
        }

        foreach (var (dependency, range) in locked.Dependencies)
        {
            Info(dependency).Requirements[locked.Name] = range;
        }
    }

    private void AddUnlockedManifest(PackageManifest manifest)
    {
        var info = Info(manifest.Name);
        info.SetUsingInfo(manifest.Version, manifest.VpmDependencies.Keys);
        info.Supersedes = [.. manifest.LegacyPackages];

        foreach (var legacy in manifest.LegacyPackages)
        {
            Info(legacy).SupersededBy.Add(manifest.Name);
        }

        foreach (var (dependency, range) in manifest.VpmDependencies)
        {
            Info(dependency).Requirements[manifest.Name] = range;
        }
    }

    private void AddUnlockedName(string name) => _unlockedNames.Add(name);

    private void Run(MissingDependencies missingDependencies)
    {
        while (_queue.TryDequeue(out var package, out var force))
        {
            if (!AddPackage(package, force))
            {
                continue;
            }

            foreach (var (dependency, range) in package.VpmDependencies)
            {
                if (!ShouldAddPackage(dependency, range))
                {
                    continue;
                }

                if (FindPackage(dependency, range, package.Version.IsPre) is { } found)
                {
                    _queue.AddPendingPackage(found);
                }
                else
                {
                    missingDependencies.Add(dependency, range);
                }
            }
        }
    }

    /// <summary>决定用这个版本。返回 false 表示这个包被跳过，它的依赖也不用管了。</summary>
    private bool AddPackage(PackageInfo package, bool force)
    {
        var entry = Info(package.Name);

        if (entry.IsSuperseded || (!force && _unlockedNames.Contains(package.Name)))
        {
            return false;
        }

        entry.Touched = true;

        var name = package.Name;
        var dependencies = package.VpmDependencies;
        var supersedes = package.Manifest.LegacyPackages;

        entry.Current = package.Version;
        entry.Using = package;

        var previousDependencies = entry.Dependencies;
        var previousSupersedes = entry.Supersedes;

        entry.Dependencies = [.. dependencies.Keys];
        entry.Supersedes = [.. supersedes];

        foreach (var dependency in previousDependencies)
        {
            Info(dependency).RemoveRange(name);
        }

        foreach (var (dependency, range) in dependencies)
        {
            Info(dependency).AddRange(name, range);
        }

        foreach (var superseded in previousSupersedes)
        {
            Info(superseded).RemoveSupersededBy(name);
        }

        foreach (var legacy in supersedes)
        {
            Info(legacy).AddSupersededBy(name);
        }

        return true;
    }

    private bool ShouldAddPackage(string name, VersionRange range)
    {
        var entry = Info(name);

        if (entry.IsSuperseded || _unlockedNames.Contains(name))
        {
            return false;
        }

        var acceptance = Acceptance(entry.AllowPrerelease || _allowPrerelease);

        if (_queue.FindPendingPackage(name) is { } pending)
        {
            return !range.Matches(pending.Version, acceptance);
        }

        return entry.Current is not { } current || !range.Matches(current, acceptance);
    }

    private PackageInfo? FindPackage(string name, VersionRange range, bool requiredByPrerelease)
    {
        PackageInfo? Find(UnityVersion? unity, PrereleaseAcceptance acceptance) =>
            _catalog.Find(name, VersionSelector.RangeFor(unity, range, acceptance));

        if (_allowPrerelease)
        {
            return Find(_unityVersion, PrereleaseAcceptance.Allow)
                ?? Find(null, PrereleaseAcceptance.Allow);
        }

        if (requiredByPrerelease)
        {
            return Find(_unityVersion, PrereleaseAcceptance.Deny)
                ?? Find(_unityVersion, PrereleaseAcceptance.Minimum)
                ?? Find(_unityVersion, PrereleaseAcceptance.Allow)
                ?? Find(null, PrereleaseAcceptance.Deny)
                ?? Find(null, PrereleaseAcceptance.Minimum)
                ?? Find(null, PrereleaseAcceptance.Allow);
        }

        return Find(_unityVersion, PrereleaseAcceptance.Deny)
            ?? Find(_unityVersion, PrereleaseAcceptance.Minimum)
            ?? Find(null, PrereleaseAcceptance.Deny)
            ?? Find(null, PrereleaseAcceptance.Minimum);
    }

    private PackageResolution BuildResult()
    {
        var conflicts = new OrderedDictionary<string, IReadOnlyList<string>>();

        foreach (var (name, info) in _dependencies)
        {
            if (info.IsSuperseded || !info.Touched || info.Current is not { } version)
            {
                continue;
            }

            var acceptance = Acceptance(info.AllowPrerelease || _allowPrerelease);

            var conflictsWithThis = info.Requirements
                .Where(x => _dependencies.GetValueOrDefault(x.Key) is { IsSuperseded: false })
                .Where(x => !x.Value.Matches(version, acceptance))
                .Select(x => x.Key)
                .ToList();

            if (conflictsWithThis.Count > 0
                && (info.Using is not null
                    || conflictsWithThis.Any(x => _dependencies.GetValueOrDefault(x)?.Using is not null)))
            {
                conflicts[name] = conflictsWithThis;
            }
        }

        return new PackageResolution(
            [.. _dependencies.Values.Where(x => !x.IsSuperseded).Select(x => x.Using).OfType<PackageInfo>()],
            conflicts,
            [.. _dependencies.Where(x => x.Value.IsSuperseded).Select(x => x.Key)]);
    }

    private DependencyInfo Info(string name)
    {
        if (!_dependencies.TryGetValue(name, out var info))
        {
            _dependencies[name] = info = new DependencyInfo();
        }

        return info;
    }

    internal static PrereleaseAcceptance Acceptance(bool allowPrerelease) =>
        allowPrerelease ? PrereleaseAcceptance.Allow : PrereleaseAcceptance.Minimum;

    private sealed class PackageQueue(IReadOnlyList<PackageInfo> packages)
    {
        private readonly List<PackageInfo> _pending = [.. packages];

        private int _forceCount = packages.Count;

        public bool TryDequeue(out PackageInfo package, out bool force)
        {
            force = _forceCount > 0;
            _forceCount = Math.Max(0, _forceCount - 1);

            if (_pending.Count == 0)
            {
                package = null!;
                return false;
            }

            package = _pending[^1];
            _pending.RemoveAt(_pending.Count - 1);
            return true;
        }

        public PackageInfo? FindPendingPackage(string name) =>
            _pending.FirstOrDefault(x => x.Name == name);

        public void AddPendingPackage(PackageInfo package)
        {
            _pending.RemoveAll(x => x.Name == package.Name);
            _pending.Add(package);
        }
    }

    private sealed class DependencyInfo
    {
        public PackageInfo? Using { get; set; }

        public PackageVersion? Current { get; set; }

        public Dictionary<string, VersionRange> Requirements { get; } = [];

        public HashSet<string> Dependencies { get; set; } = [];

        public HashSet<string> SupersededBy { get; } = [];

        public HashSet<string> Supersedes { get; set; } = [];

        public bool AllowPrerelease { get; set; }

        public bool Touched { get; set; }

        public bool IsSuperseded => SupersededBy.Count > 0;

        public void SetUsingInfo(PackageVersion version, IEnumerable<string> dependencies)
        {
            AllowPrerelease |= version.IsPre;
            Current = version;
            Dependencies = [.. dependencies];
        }

        public void AddRange(string source, VersionRange range)
        {
            Requirements[source] = range;
            Touched = true;
        }

        public void RemoveRange(string source)
        {
            Requirements.Remove(source);
            Touched = true;
        }

        public void AddSupersededBy(string name)
        {
            SupersededBy.Add(name);
            Touched = true;
        }

        public void RemoveSupersededBy(string name)
        {
            SupersededBy.Remove(name);
            Touched = true;
        }
    }
}
