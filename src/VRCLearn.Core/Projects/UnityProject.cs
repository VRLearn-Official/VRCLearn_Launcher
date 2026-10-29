using VRCLearn.Core.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

public sealed class UnityProject
{
    public const string PackagesDirectory = "Packages";
    public const string ProjectSettingsAsset = "ProjectSettings/ProjectSettings.asset";
    public const string ProjectVersionPath = "ProjectSettings/ProjectVersion.txt";

    private readonly VpmManifest _manifest;
    private readonly UpmManifest _upmManifest;
    private readonly List<(string Name, PackageManifest? Manifest)> _unlockedPackages;
    private readonly Dictionary<string, PackageManifest> _installedPackages;

    private UnityProject(
        IFileIo io,
        VpmManifest manifest,
        UpmManifest upmManifest,
        UnityVersion unityVersion,
        string? unityRevision,
        List<(string Name, PackageManifest? Manifest)> unlockedPackages,
        Dictionary<string, PackageManifest> installedPackages)
    {
        Io = io;
        _manifest = manifest;
        _upmManifest = upmManifest;
        UnityVersion = unityVersion;
        UnityRevision = unityRevision;
        _unlockedPackages = unlockedPackages;
        _installedPackages = installedPackages;
    }

    public IFileIo Io { get; }

    internal VpmManifest Manifest => _manifest;

    public UnityVersion UnityVersion { get; }

    public string? UnityRevision { get; }

    public string ProjectDirectory => Io.RootDirectory;

    public IEnumerable<LockedDependencyInfo> LockedPackages => _manifest.AllLocked();

    /// <summary><c>dependencies</c> 段：用户声明的范围，不一定是实际装的版本。</summary>
    public IEnumerable<KeyValuePair<string, DependencyRange>> Dependencies => _manifest.Dependencies;

    /// <summary>已登记 + 存在但未登记的包。</summary>
    public IEnumerable<LockedDependencyInfo> AllPackages => LockedPackages.Concat(
        _unlockedPackages
            .Where(x => x.Manifest is not null)
            .Select(x => LockedDependencyInfo.Create(
                x.Manifest!.Name,
                x.Manifest.Version,
                x.Manifest.VpmDependencies)));

    /// <summary>Packages 目录下存在、但没登记进 vpm-manifest.json 的包。</summary>
    public IReadOnlyList<(string Name, PackageManifest? Manifest)> UnlockedPackages => _unlockedPackages;

    public IEnumerable<KeyValuePair<string, PackageManifest>> InstalledPackages => _installedPackages;

    public IEnumerable<PackageManifest> AllInstalledPackages =>
        _installedPackages.Values.Concat(_unlockedPackages.Select(x => x.Manifest).OfType<PackageManifest>());

    public static async Task<UnityProject> LoadAsync(IFileIo io, CancellationToken cancellationToken)
    {
        var manifest = await VpmManifest.LoadAsync(io, cancellationToken).ConfigureAwait(false);
        var upmManifest = await UpmManifest.LoadAsync(io, cancellationToken).ConfigureAwait(false);
        var (unityVersion, unityRevision) = await ReadUnityVersionAsync(io, cancellationToken).ConfigureAwait(false);

        var installedPackages = new Dictionary<string, PackageManifest>();
        var unlockedPackages = new List<(string Name, PackageManifest? Manifest)>();

        foreach (var name in io.ListDirectories(PackagesDirectory))
        {
            var parsed = await TryReadPackageAsync(io, name, cancellationToken).ConfigureAwait(false);

            if (parsed is not null && parsed.Name == name && manifest.GetLocked(parsed.Name) is not null)
            {
                installedPackages[name] = parsed;
            }
            else
            {
                unlockedPackages.Add((name, parsed));
            }
        }

        return new UnityProject(
            io,
            manifest,
            upmManifest,
            unityVersion,
            unityRevision,
            unlockedPackages,
            installedPackages);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => Task.WhenAll(
        _manifest.SaveAsync(Io, cancellationToken),
        _upmManifest.SaveAsync(Io, cancellationToken));

    /// <summary>只重读清单；已装/未登记的包列表是加载时扫出来的，改过 Packages 目录后要重新 LoadAsync。</summary>
    internal Task ReloadManifestsAsync(CancellationToken cancellationToken) => Task.WhenAll(
        _manifest.ReloadAsync(Io, cancellationToken),
        _upmManifest.ReloadAsync(Io, cancellationToken));

    public ProjectType DetectProjectType()
    {
        if (IsLocked("com.vrchat.avatars"))
        {
            return ProjectType.Avatars;
        }

        if (IsLocked("com.vrchat.worlds"))
        {
            return ProjectType.Worlds;
        }

        if (_manifest.HasAny)
        {
            return ProjectType.VpmStarter;
        }

        if (HasUpmPackage("com.vrchat.avatars"))
        {
            return ProjectType.UpmAvatars;
        }

        if (HasUpmPackage("com.vrchat.worlds"))
        {
            return ProjectType.UpmWorlds;
        }

        if (HasUpmPackage("com.vrchat.base"))
        {
            return ProjectType.UpmStarter;
        }

        return LegacyDllType() ?? ProjectType.Unknown;
    }

    private ProjectType? LegacyDllType()
    {
        if (Io.FileExists("Assets/VRCSDK/Plugins/VRCSDK2.dll"))
        {
            return ProjectType.LegacySdk2;
        }

        if (Io.FileExists("Assets/VRCSDK/Plugins/VRCSDK3.dll"))
        {
            return ProjectType.LegacyWorlds;
        }

        return Io.FileExists("Assets/VRCSDK/Plugins/VRCSDK3A.dll")
            ? ProjectType.LegacyAvatars
            : null;
    }

    /// <param name="customPackages">不要求安装或版本对齐的锁定包，仓库目录里没有它们。</param>
    public bool ShouldResolve(IReadOnlySet<string>? customPackages = null)
    {
        var installedOrSuperseded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var locked in LockedPackages)
        {
            var custom = customPackages?.Contains(locked.Name) ?? false;

            if (!_installedPackages.TryGetValue(locked.Name, out var installed))
            {
                if (custom)
                {
                    installedOrSuperseded.Add(locked.Name);
                    continue;
                }

                return true;
            }

            if (!custom && !installed.Version.Equals(locked.Version))
            {
                return true;
            }

            installedOrSuperseded.Add(locked.Name);

            foreach (var superseded in installed.LegacyPackages)
            {
                installedOrSuperseded.Add(superseded);
            }
        }

        foreach (var manifest in _unlockedPackages.Select(x => x.Manifest).OfType<PackageManifest>())
        {
            foreach (var superseded in manifest.LegacyPackages)
            {
                installedOrSuperseded.Add(superseded);
            }

            installedOrSuperseded.Add(manifest.Name);
        }

        if (_manifest.Dependencies.Any(x => !installedOrSuperseded.Contains(x.Key)))
        {
            return true;
        }

        return _unlockedPackages
            .Select(x => x.Manifest)
            .OfType<PackageManifest>()
            .Any(manifest => manifest.VpmDependencies.Keys.Any(x => !installedOrSuperseded.Contains(x)));
    }

    public LockedDependencyInfo? GetLocked(string name) => _manifest.GetLocked(name);

    public DependencyRange? GetDependency(string name) => _manifest.GetDependency(name);

    public bool IsLocked(string name) => _manifest.GetLocked(name) is not null;

    public PackageManifest? GetInstalledPackage(string name) => _installedPackages.GetValueOrDefault(name);

    public bool HasUpmPackage(string name) => _upmManifest.HasDependency(name);

    public void RemoveUpmDependency(string name) => _upmManifest.RemoveDependency(name);

    /// <summary>只改清单，不真正装包。</summary>
    public void AddDependencyRaw(string name, DependencyRange version) => _manifest.AddDependency(name, version);

    private static async Task<PackageManifest?> TryReadPackageAsync(
        IFileIo io,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await ProjectJson
                .ReadOrNullAsync<JsonObject>(io, $"{PackagesDirectory}/{name}/package.json", cancellationToken)
                .ConfigureAwait(false);

            return json is null ? null : LoosePackageManifest.TryRead(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<(UnityVersion Version, string? Revision)> ReadUnityVersionAsync(
        IFileIo io,
        CancellationToken cancellationToken)
    {
        var bytes = await io.ReadAllBytesOrNullAsync(ProjectVersionPath, cancellationToken).ConfigureAwait(false);

        if (bytes is null)
        {
            throw new FileNotFoundException($"Error opening {ProjectVersionPath}", ProjectVersionPath);
        }

        var content = System.Text.Encoding.UTF8.GetString(bytes);

        var versionText = FindAttribute(content, "m_EditorVersion:")
            ?? throw new InvalidDataException("Failed to parse m_EditorVersion: No m_EditorVersion was found");

        var version = UnityVersion.TryParse(versionText)
            ?? throw new InvalidDataException($"Failed to parse m_EditorVersion: {versionText}");

        string? revision = null;
        if (FindAttribute(content, "m_EditorVersionWithRevision:") is { } revisionInfo)
        {
            revision = ParseVersionWithRevision(revisionInfo)
                ?? throw new InvalidDataException("Failed to parse m_EditorVersionWithRevision");
        }

        return (version, revision);
    }

    private static string? FindAttribute(string content, string attribute)
    {
        var start = content.IndexOf(attribute, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var value = content[(start + attribute.Length)..];
        var end = value.IndexOfAny(['\r', '\n']);
        if (end >= 0)
        {
            value = value[..end];
        }

        return value.Trim();
    }

    private static string? ParseVersionWithRevision(string versionInfo)
    {
        var open = versionInfo.IndexOf('(');
        if (open < 0)
        {
            return null;
        }

        var rest = versionInfo[(open + 1)..];
        var close = rest.IndexOf(')');
        return close < 0 ? null : rest[..close];
    }
}
