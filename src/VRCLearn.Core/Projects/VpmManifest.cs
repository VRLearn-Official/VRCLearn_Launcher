using VRCLearn.Core.IO;
using System.Text.Json.Serialization;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Projects;

/// <summary>项目的包清单 <c>Packages/vpm-manifest.json</c>，写回时保持插入顺序。</summary>
public sealed class VpmManifest
{
    public const string RelativePath = "Packages/vpm-manifest.json";

    private SaveController<VpmManifestJson> _controller;

    private VpmManifest(VpmManifestJson json) => _controller = new SaveController<VpmManifestJson>(json);

    public static async Task<VpmManifest> LoadAsync(IFileIo io, CancellationToken cancellationToken) =>
        new(await ReadAsync(io, cancellationToken).ConfigureAwait(false));

    public async Task ReloadAsync(IFileIo io, CancellationToken cancellationToken) =>
        _controller = new SaveController<VpmManifestJson>(
            await ReadAsync(io, cancellationToken).ConfigureAwait(false));

    private static Task<VpmManifestJson> ReadAsync(IFileIo io, CancellationToken cancellationToken) =>
        ProjectJson.LoadOrDefaultAsync(io, RelativePath, static () => new VpmManifestJson(), cancellationToken);

    public IEnumerable<KeyValuePair<string, DependencyRange>> Dependencies =>
        _controller.Value.Dependencies.Select(x => new KeyValuePair<string, DependencyRange>(x.Key, x.Value.Version));

    public DependencyRange? GetDependency(string package) =>
        _controller.Value.Dependencies.TryGetValue(package, out var dependency) ? dependency.Version : null;

    public IEnumerable<LockedDependencyInfo> AllLocked() =>
        _controller.Value.Locked.Select(x => LockedDependencyInfo.Create(x.Key, x.Value.Version, x.Value.Dependencies));

    public LockedDependencyInfo? GetLocked(string package) =>
        _controller.Value.Locked.TryGetValue(package, out var locked)
            ? LockedDependencyInfo.Create(package, locked.Version, locked.Dependencies)
            : null;

    public bool HasAny => _controller.Value.Locked.Count > 0 || _controller.Value.Dependencies.Count > 0;

    public void AddDependency(string name, DependencyRange version) =>
        _controller.AsMutable().Dependencies[name] = new VpmDependencyJson { Version = version };

    public void AddLocked(
        string name,
        PackageVersion version,
        IReadOnlyDictionary<string, VersionRange> dependencies) =>
        _controller.AsMutable().Locked[name] = new VpmLockedDependencyJson
        {
            Version = version,
            Dependencies = new OrderedDictionary<string, VersionRange>(dependencies),
        };

    public void RemovePackages(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            _controller.AsMutable().Locked.Remove(name);
            _controller.AsMutable().Dependencies.Remove(name);
        }
    }

    public Task SaveAsync(IFileIo io, CancellationToken cancellationToken) =>
        _controller.SaveAsync(
            json => ProjectJson.SaveAsync(io, RelativePath, json, cancellationToken));
}

internal sealed class VpmManifestJson
{
    [JsonPropertyName("dependencies")]
    public OrderedDictionary<string, VpmDependencyJson> Dependencies { get; set; } = new();

    [JsonPropertyName("locked")]
    public OrderedDictionary<string, VpmLockedDependencyJson> Locked { get; set; } = new();
}

internal sealed class VpmDependencyJson
{
    [JsonPropertyName("version")]
    public required DependencyRange Version { get; init; }
}

internal sealed class VpmLockedDependencyJson
{
    [JsonPropertyName("version")]
    public required PackageVersion Version { get; init; }

    [JsonPropertyName("dependencies")]
    public OrderedDictionary<string, VersionRange>? Dependencies { get; init; }
}
