using VRCLearn.Core.Packages;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

public enum AddPackageStatus
{
    Success,

    MissingDependencies,

    NotInstalled,

    NotADowngrade,

    VersionNotFound,
}

/// <summary>装包时怎么处理项目里已有的同名包。</summary>
public enum AddPackageOperation
{
    /// <summary>按现状自动决定：没装过就装上，装过则升级 / 重装 / 降级。</summary>
    AutoDetected,

    /// <summary>只把它记进 <c>dependencies</c>，并且在项目里没有同等或更新版本时才真的装。</summary>
    InstallToDependencies,

    /// <summary>只升级，降级不算数。</summary>
    UpgradeLocked,

    /// <summary>只降级，升级不算数。</summary>
    Downgrade,
}

public enum ResolveStatus
{
    Success,

    MissingDependencies,
}

public sealed record MissingDependency(string Name, VersionRange Range);

public sealed record AddPackageOutcome(
    AddPackageStatus Status,
    PendingProjectChanges? Changes,
    IReadOnlyList<MissingDependency> MissingDependencies);

public enum RemovePackageStatus
{
    Success,

    NotInstalled,
}

public sealed record RemovePackageOutcome(
    RemovePackageStatus Status,
    PendingProjectChanges? Changes,
    IReadOnlyList<string> NotInstalled);

public sealed record ResolveOutcome(
    ResolveStatus Status,
    PendingProjectChanges? Changes,
    IReadOnlyList<MissingDependency> MissingDependencies);

public enum ReinstallStatus
{
    Success,

    NotInstalled,

    MissingDependencies,
}

public sealed record ReinstallOutcome(
    ReinstallStatus Status,
    PendingProjectChanges? Changes,
    IReadOnlyList<string> NotInstalled,
    IReadOnlyList<MissingDependency> MissingDependencies);

/// <summary>收集「装包 / 删包 / 捋顺」的变更，只算不动盘。</summary>
public static class PackageOperations
{
    private static readonly IReadOnlySet<string> EmptyNames = new HashSet<string>();

    /// <summary>装指定的这些版本，依赖由 <see cref="PackageResolver"/> 定。</summary>
    public static async Task<AddPackageOutcome> AddAsync(
        UnityProject project,
        PackageCatalog catalog,
        IReadOnlyList<PackageInfo> packages,
        AddPackageOperation operation,
        bool allowPrerelease,
        CancellationToken cancellationToken)
    {
        var builder = new PendingChangesBuilder();
        var toInstall = new List<PackageInfo>();

        foreach (var package in packages)
        {
            var status = PlanInstallation(project, builder, package, operation, toInstall);

            if (status != AddPackageStatus.Success)
            {
                return new AddPackageOutcome(status, null, []);
            }
        }

        if (toInstall.Count == 0)
        {
            return new AddPackageOutcome(
                AddPackageStatus.Success,
                builder.BuildNoResolve(),
                []);
        }

        var missing = new MissingDependencies();

        var resolution = PackageResolver.Collect(
            catalog,
            RootDependencies(project, builder),
            project.LockedPackages,
            project.UnlockedPackages,
            project.GetLocked,
            project.UnityVersion,
            toInstall,
            allowPrerelease,
            missing);

        if (!missing.IsEmpty)
        {
            return new AddPackageOutcome(AddPackageStatus.MissingDependencies, null, missing.Entries);
        }

        foreach (var resolved in resolution.NewPackages)
        {
            builder.InstallToLocked(resolved);
            ReportUnlockedCollisions(project, builder, resolved.Name);
        }

        foreach (var (name, conflicts) in resolution.Conflicts)
        {
            builder.ConflictMultiple(name, conflicts);
        }

        foreach (var name in resolution.FoundLegacyPackages.Where(project.IsLocked))
        {
            builder.RemovePackage(name, RemoveReason.Legacy);
        }

        return new AddPackageOutcome(
            AddPackageStatus.Success,
            await builder.BuildResolveAsync(project, cancellationToken).ConfigureAwait(false),
            []);
    }

    private static AddPackageStatus PlanInstallation(
        UnityProject project,
        PendingChangesBuilder builder,
        PackageInfo package,
        AddPackageOperation operation,
        List<PackageInfo> toInstall)
    {
        var locked = project.GetLocked(package.Name);

        switch (operation)
        {
            case AddPackageOperation.AutoDetected when locked is null:
            case AddPackageOperation.InstallToDependencies:
                return InstallToDependencies(project, builder, package, toInstall);

            case AddPackageOperation.AutoDetected:
                var comparison = locked!.Version.CompareTo(package.Version);

                if (comparison < 0)
                {
                    return UpgradeLocked(project, package, toInstall);
                }

                return comparison == 0
                    ? MarkForReinstall(package, toInstall)
                    : Downgrade(project, builder, package, locked, toInstall);

            case AddPackageOperation.UpgradeLocked when locked is null:
            case AddPackageOperation.Downgrade when locked is null:
                return AddPackageStatus.NotInstalled;

            case AddPackageOperation.UpgradeLocked:
                return UpgradeLocked(project, package, toInstall);

            default:
                return Downgrade(project, builder, package, locked!, toInstall);
        }
    }

    private static AddPackageStatus InstallToDependencies(
        UnityProject project,
        PendingChangesBuilder builder,
        PackageInfo package,
        List<PackageInfo> toInstall)
    {
        if (ShouldRecordDependency(project, package))
        {
            builder.AddToDependencies(package.Name, DependencyRange.FromVersion(package.Version));
        }

        return AddIfNotNewer(project, package, toInstall);
    }

    private static AddPackageStatus UpgradeLocked(
        UnityProject project,
        PackageInfo package,
        List<PackageInfo> toInstall) =>
        AddIfNotNewer(project, package, toInstall);

    private static AddPackageStatus MarkForReinstall(PackageInfo package, List<PackageInfo> toInstall)
    {
        toInstall.Add(package);
        return AddPackageStatus.Success;
    }

    private static AddPackageStatus Downgrade(
        UnityProject project,
        PendingChangesBuilder builder,
        PackageInfo package,
        LockedDependencyInfo locked,
        List<PackageInfo> toInstall)
    {
        if (locked.Version < package.Version)
        {
            return AddPackageStatus.NotADowngrade;
        }

        if (project.GetDependency(package.Name) is { } declared && !declared.Matches(package.Version))
        {
            builder.AddToDependencies(package.Name, DependencyRange.FromVersion(package.Version));
        }

        toInstall.Add(package);
        return AddPackageStatus.Success;
    }

    private static AddPackageStatus AddIfNotNewer(
        UnityProject project,
        PackageInfo package,
        List<PackageInfo> toInstall)
    {
        if (project.GetLocked(package.Name) is not { } locked || locked.Version < package.Version)
        {
            toInstall.Add(package);
        }

        return AddPackageStatus.Success;
    }

    public static Task<AddPackageOutcome> SwitchVersionAsync(
        UnityProject project,
        PackageCatalog catalog,
        string name,
        PackageVersion version,
        bool allowPrerelease,
        CancellationToken cancellationToken) =>
        catalog.Find(name, VersionSelector.ForSpecificVersion(version)) is not { } package
            ? Task.FromResult(new AddPackageOutcome(AddPackageStatus.VersionNotFound, null, []))
            : AddAsync(project, catalog, [package], AddPackageOperation.AutoDetected, allowPrerelease, cancellationToken);

    public static async Task<ReinstallOutcome> ReinstallAsync(
        UnityProject project,
        PackageCatalog catalog,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var builder = new PendingChangesBuilder();
        var missing = new MissingDependencies();

        foreach (var name in names)
        {
            if (project.GetLocked(name) is not { } locked)
            {
                return new ReinstallOutcome(ReinstallStatus.NotInstalled, null, [name], []);
            }

            if (catalog.Find(name, VersionSelector.ForSpecificVersion(locked.Version)) is { } package)
            {
                builder.InstallAlreadyLocked(package);
            }
            else
            {
                missing.Add(name, VersionRange.Specific(locked.Version));
            }
        }

        if (!missing.IsEmpty)
        {
            return new ReinstallOutcome(ReinstallStatus.MissingDependencies, null, [], missing.Entries);
        }

        return new ReinstallOutcome(
            ReinstallStatus.Success,
            await builder.BuildResolveAsync(project, cancellationToken).ConfigureAwait(false),
            [],
            []);
    }

    /// <summary>只删清单里登记过的包，不动未登记目录。</summary>
    public static async Task<RemovePackageOutcome> RemoveAsync(
        UnityProject project,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var notInstalled = names.Where(name => project.GetLocked(name) is null).ToList();

        if (notInstalled.Count > 0)
        {
            return new RemovePackageOutcome(RemovePackageStatus.NotInstalled, null, notInstalled);
        }

        var builder = new PendingChangesBuilder();

        CollectRemovalConflicts(project, names, builder);

        foreach (var name in names)
        {
            builder.RemovePackage(name, RemoveReason.Requested);
        }

        return new RemovePackageOutcome(
            RemovePackageStatus.Success,
            await builder.BuildResolveAsync(project, cancellationToken).ConfigureAwait(false),
            []);
    }

    public static Task<ResolveOutcome> ResolveAsync(
        UnityProject project,
        PackageCatalog catalog,
        CancellationToken cancellationToken) =>
        ResolveAsync(project, catalog, null, cancellationToken);

    public static async Task<ResolveOutcome> ResolveAsync(
        UnityProject project,
        PackageCatalog catalog,
        IReadOnlySet<string>? customPackages,
        CancellationToken cancellationToken)
    {
        var missing = new MissingDependencies();

        var lockedPackages = new List<PackageInfo>();

        foreach (var locked in project.LockedPackages)
        {
            if (customPackages?.Contains(locked.Name) == true)
            {
                continue;
            }

            if (catalog.Find(locked.Name, VersionSelector.ForSpecificVersion(locked.Version)) is { } package)
            {
                lockedPackages.Add(package);
            }
            else
            {
                missing.Add(locked.Name, VersionRange.Specific(locked.Version));
            }
        }

        var justDeclared = CollectJustDeclared(project, catalog, missing);
        var unlockedDependencies = CollectUnlockedDependencies(project, catalog, missing, justDeclared.Installing);

        if (!missing.IsEmpty)
        {
            return new ResolveOutcome(ResolveStatus.MissingDependencies, null, missing.Entries);
        }

        var builder = new PendingChangesBuilder();

        foreach (var package in lockedPackages)
        {
            builder.InstallAlreadyLocked(package);
        }

        ApplyResolution(project, builder, justDeclared.Resolution, justDeclared.DeclaredNames);
        ApplyResolution(project, builder, unlockedDependencies, EmptyNames);

        return new ResolveOutcome(
            ResolveStatus.Success,
            await builder.BuildResolveAsync(project, cancellationToken).ConfigureAwait(false),
            []);
    }

    private readonly record struct JustDeclared(
        PackageResolution? Resolution,
        IReadOnlySet<string> DeclaredNames,
        IReadOnlyList<PackageInfo> Installing);

    private static JustDeclared CollectJustDeclared(
        UnityProject project,
        PackageCatalog catalog,
        MissingDependencies missing)
    {
        var toInstall = new List<PackageInfo>();
        var declaredNames = new HashSet<string>();

        foreach (var (name, dependency) in project.Dependencies)
        {
            if (project.GetLocked(name) is not null)
            {
                continue;
            }

            var range = dependency.AsRange();

            if (catalog.Find(
                name,
                VersionSelector.RangeFor(project.UnityVersion, range, PackageResolver.Acceptance(range.ContainsPre))) is { } package)
            {
                toInstall.Add(package);
            }
            else
            {
                missing.Add(name, range);
            }

            declaredNames.Add(name);
        }

        if (toInstall.Count == 0)
        {
            return new JustDeclared(null, declaredNames, []);
        }

        return new JustDeclared(
            Resolve(project, catalog, toInstall, project.Dependencies, missing),
            declaredNames,
            toInstall);
    }

    private static PackageResolution? CollectUnlockedDependencies(
        UnityProject project,
        PackageCatalog catalog,
        MissingDependencies missing,
        IReadOnlyList<PackageInfo> alreadyInstalling)
    {
        var unlockedNames = project.UnlockedPackages
            .Select(x => x.Manifest?.Name)
            .OfType<string>()
            .ToHashSet();

        if (unlockedNames.Count == 0)
        {
            return null;
        }

        var installing = alreadyInstalling.Select(x => x.Name).ToHashSet();

        var wanted = new OrderedDictionary<string, List<(VersionRange Range, bool IsPrerelease)>>();

        foreach (var (_, manifest) in project.UnlockedPackages)
        {
            if (manifest is null)
            {
                continue;
            }

            foreach (var (name, range) in manifest.VpmDependencies)
            {
                if (project.GetLocked(name) is not null
                    || installing.Contains(name)
                    || unlockedNames.Contains(name))
                {
                    continue;
                }

                if (!wanted.TryGetValue(name, out var requests))
                {
                    wanted[name] = requests = [];
                }

                requests.Add((range, manifest.Version.IsPre));
            }
        }

        if (wanted.Count == 0)
        {
            return null;
        }

        var toInstall = new List<PackageInfo>();

        foreach (var (name, requests) in wanted)
        {
            var ranges = requests.Select(x => x.Range).ToList();

            if (catalog.Find(
                name,
                VersionSelector.RangesFor(
                    project.UnityVersion,
                    ranges,
                    PackageResolver.Acceptance(requests.Any(x => x.IsPrerelease)))) is { } package)
            {
                toInstall.Add(package);
            }
            else
            {
                missing.Add(name, ranges.Aggregate((left, right) => left.Intersect(right)));
            }
        }

        if (toInstall.Count == 0)
        {
            return null;
        }

        var virtualLocked = new OrderedDictionary<string, LockedDependencyInfo>();

        foreach (var locked in project.LockedPackages)
        {
            virtualLocked[locked.Name] = locked;
        }

        foreach (var package in alreadyInstalling)
        {
            virtualLocked[package.Name] = LockedDependencyInfo.Create(
                package.Name,
                package.Version,
                package.VpmDependencies);
        }

        var resolution = PackageResolver.Collect(
            catalog,
            project.Dependencies,
            virtualLocked.Values,
            project.UnlockedPackages,
            name => virtualLocked.GetValueOrDefault(name),
            project.UnityVersion,
            toInstall,
            toInstall.Any(x => x.Version.IsPre),
            missing);

        return resolution;
    }

    private static PackageResolution Resolve(
        UnityProject project,
        PackageCatalog catalog,
        IReadOnlyList<PackageInfo> toInstall,
        IEnumerable<KeyValuePair<string, DependencyRange>> rootDependencies,
        MissingDependencies missing) =>
        PackageResolver.Collect(
            catalog,
            rootDependencies,
            project.LockedPackages,
            project.UnlockedPackages,
            project.GetLocked,
            project.UnityVersion,
            toInstall,
            toInstall.Any(x => x.Version.IsPre),
            missing);

    private static void ApplyResolution(
        UnityProject project,
        PendingChangesBuilder builder,
        PackageResolution? resolution,
        IReadOnlySet<string> declaredNames)
    {
        if (resolution is null)
        {
            return;
        }

        foreach (var package in resolution.NewPackages)
        {
            builder.InstallToLockedReplacing(package);

            if (declaredNames.Contains(package.Name))
            {
                builder.AddToDependencies(package.Name, DependencyRange.FromVersion(package.Version));
            }

            ReportUnlockedCollisions(project, builder, package.Name);
        }

        foreach (var (name, conflicts) in resolution.Conflicts)
        {
            builder.ConflictMultiple(name, conflicts);
        }
    }

    private static void ReportUnlockedCollisions(
        UnityProject project,
        PendingChangesBuilder builder,
        string name)
    {
        foreach (var (directory, manifest) in project.UnlockedPackages)
        {
            if (directory == name || manifest?.Name == name)
            {
                builder.UnlockedInstallationConflict(name, directory);
            }
        }
    }

    private static IEnumerable<KeyValuePair<string, DependencyRange>> RootDependencies(
        UnityProject project,
        PendingChangesBuilder builder)
    {
        foreach (var (name, declared) in project.Dependencies)
        {
            yield return new KeyValuePair<string, DependencyRange>(
                name,
                builder.GetDependencies(name) ?? declared);
        }
    }

    private static bool ShouldRecordDependency(UnityProject project, PackageInfo package) =>
        project.GetDependency(package.Name)?.AsSingleVersion() is not { } declared
        || declared < package.Version;

    private static void CollectRemovalConflicts(
        UnityProject project,
        IReadOnlyList<string> names,
        PendingChangesBuilder builder)
    {
        var mayConflict = new HashSet<string>(names);

        foreach (var manifest in project.AllInstalledPackages.Where(x => !names.Contains(x.Name)))
        {
            foreach (var superseded in manifest.LegacyPackages)
            {
                mayConflict.Remove(superseded);
            }
        }

        foreach (var installed in project.AllPackages.Where(x => !names.Contains(x.Name)))
        {
            foreach (var conflict in mayConflict.Where(installed.Dependencies.ContainsKey))
            {
                builder.Conflicts(conflict, installed.Name);
            }
        }
    }

}
