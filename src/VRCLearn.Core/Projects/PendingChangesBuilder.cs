using VRCLearn.Core.Packages;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

public sealed class PendingChangesBuilder
{
    private readonly OrderedDictionary<string, PackageChange> _packageChanges = [];
    private readonly OrderedDictionary<string, ConflictInfo> _conflicts = [];

    public PendingChangesBuilder AddToDependencies(string name, DependencyRange version)
    {
        switch (_packageChanges.GetValueOrDefault(name))
        {
            case Install { ToDependencies: null } install:
                install.ToDependencies = version;
                break;

            case Install:
                throw new InvalidOperationException($"已经为 {name} 添加过依赖了");

            case Remove:
                throw new InvalidOperationException($"{name} 已被标记移除，不能再添加依赖");

            default:
                _packageChanges[name] = new Install(null, addToLocked: false, version);
                break;
        }

        return this;
    }

    public PendingChangesBuilder InstallToLocked(PackageInfo package) =>
        SetInstall(package, addToLocked: true, allowReplace: false);

    public PendingChangesBuilder InstallToLockedReplacing(PackageInfo package) =>
        SetInstall(package, addToLocked: true, allowReplace: true);

    public PendingChangesBuilder InstallAlreadyLocked(PackageInfo package) =>
        SetInstall(package, addToLocked: false, allowReplace: false);

    public PendingChangesBuilder UnlockedInstallationConflict(string name, string unlockedName)
    {
        ConflictsOf(name).AddUnlocked(unlockedName);
        return this;
    }

    public PendingChangesBuilder Conflicts(string name, string conflict)
    {
        ConflictsOf(name).AddConflicting([conflict]);
        return this;
    }

    public PendingChangesBuilder ConflictMultiple(string name, IEnumerable<string> conflicts)
    {
        ConflictsOf(name).AddConflicting(conflicts);
        return this;
    }

    public PendingChangesBuilder ConflictsUnity(string name)
    {
        ConflictsOf(name).ConflictsWithUnity = true;
        return this;
    }

    public PendingChangesBuilder RemovePackage(string name, RemoveReason reason)
    {
        switch (_packageChanges.GetValueOrDefault(name))
        {
            case Install:
                throw new InvalidOperationException($"{name} 正在安装，不能同时移除");

            case Remove existing when existing.Reason != reason:
                throw new InvalidOperationException($"{name} 已经因其他原因被标记移除");

            case Remove:
                break;

            default:
                _packageChanges[name] = new Remove(reason);
                break;
        }

        return this;
    }

    public DependencyRange? GetDependencies(string name) =>
        _packageChanges.GetValueOrDefault(name)?.AsInstall?.ToDependencies;

    public PendingProjectChanges BuildNoResolve()
    {
        foreach (var (name, change) in _packageChanges)
        {
            if (change is not Install { Package: null })
            {
                throw new InvalidOperationException($"{name} 需要先做依赖解析");
            }
        }

        return new PendingProjectChanges(_packageChanges, _conflicts, [], []);
    }

    public async Task<PendingProjectChanges> BuildResolveAsync(
        UnityProject project,
        CancellationToken cancellationToken)
    {
        var installs = _packageChanges.Values
            .OfType<Install>()
            .Where(x => x.AddToLocked)
            .Select(x => x.Package!)
            .ToList();

        foreach (var package in installs)
        {
            if (!UnityCompatibility.IsCompatible(package.Manifest, project.UnityVersion))
            {
                ConflictsUnity(package.Name);
            }
        }

        MarkAndSweepPackages(project);

        var legacy = await LegacyAssetFinder
            .FindAsync(project.Io, installs, project, cancellationToken)
            .ConfigureAwait(false);

        return new PendingProjectChanges(
            _packageChanges,
            _conflicts,
            legacy.Files,
            legacy.Folders);
    }

    private void MarkAndSweepPackages(UnityProject project)
    {
        var entryPoints = new List<string>();

        foreach (var (name, change) in _packageChanges)
        {
            if (project.GetLocked(name) is not { } locked)
            {
                continue;
            }

            if (change is Install install && !install.AddToLocked)
            {
                continue;
            }

            entryPoints.Add(locked.Name);
        }

        var removable = Mark(entryPoints, dep => DependenciesOf(project, dep));

        if (removable.Count == 0)
        {
            return;
        }

        var roots = new List<string>();

        foreach (var (_, manifest) in project.UnlockedPackages)
        {
            if (manifest is not null)
            {
                roots.AddRange(manifest.VpmDependencies.Keys);
            }
        }

        foreach (var (dependency, _) in project.Dependencies)
        {
            if (_packageChanges.GetValueOrDefault(dependency) is not Remove)
            {
                roots.Add(dependency);
            }
        }

        roots.AddRange(_packageChanges
            .Where(x => x.Value is Install)
            .Select(x => x.Key));

        var usingPackages = Mark(roots, dep =>
            _packageChanges.GetValueOrDefault(dep) is Install { Package: { } package }
                ? package.VpmDependencies.Keys
                : DependenciesOf(project, dep));

        foreach (var locked in project.LockedPackages)
        {
            if (removable.Contains(locked.Name) && !usingPackages.Contains(locked.Name))
            {
                RemoveUnused(locked.Name);
            }
        }
    }

    private static HashSet<string> Mark(IEnumerable<string> entryPoints, Func<string, IEnumerable<string>> dependencies)
    {
        var marked = new HashSet<string>(entryPoints);

        var queue = new Queue<string>(marked);

        while (queue.Count > 0)
        {
            foreach (var dependency in dependencies(queue.Dequeue()))
            {
                if (marked.Add(dependency))
                {
                    queue.Enqueue(dependency);
                }
            }
        }

        return marked;
    }

    private static IEnumerable<string> DependenciesOf(UnityProject project, string name) =>
        project.GetLocked(name)?.Dependencies.Keys ?? [];

    private void RemoveUnused(string name)
    {
        switch (_packageChanges.GetValueOrDefault(name))
        {
            case Install:
                throw new InvalidOperationException($"{name} 正在安装，不能同时回收");

            case Remove:
                break;

            default:
                _packageChanges[name] = new Remove(RemoveReason.Unused);
                break;
        }
    }

    private PendingChangesBuilder SetInstall(PackageInfo package, bool addToLocked, bool allowReplace)
    {
        switch (_packageChanges.GetValueOrDefault(package.Name))
        {
            case Install install:
                if (install.Package is not null && !allowReplace)
                {
                    throw new InvalidOperationException($"{package.Name} 已经在安装了");
                }

                install.Package = package;
                install.AddToLocked = addToLocked;
                break;

            case Remove:
                throw new InvalidOperationException($"{package.Name} 已被标记移除，不能再安装");

            default:
                _packageChanges[package.Name] = new Install(package, addToLocked, null);
                break;
        }

        return this;
    }

    private ConflictInfo ConflictsOf(string name)
    {
        if (!_conflicts.TryGetValue(name, out var conflicts))
        {
            _conflicts[name] = conflicts = new ConflictInfo();
        }

        return conflicts;
    }
}
