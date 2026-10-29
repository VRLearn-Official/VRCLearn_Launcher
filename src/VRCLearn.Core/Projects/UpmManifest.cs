using VRCLearn.Core.IO;
using System.Text.Json.Nodes;

namespace VRCLearn.Core.Projects;

/// <summary><c>Packages/manifest.json</c>。原始文档完整保留，只动 dependencies，其余字段原样写回。</summary>
public sealed class UpmManifest
{
    public const string RelativePath = "Packages/manifest.json";

    private SaveController<JsonObject> _controller;

    private UpmManifest(JsonObject raw) => _controller = new SaveController<JsonObject>(raw);

    public static async Task<UpmManifest> LoadAsync(IFileIo io, CancellationToken cancellationToken) =>
        new(await ReadAsync(io, cancellationToken).ConfigureAwait(false));

    public async Task ReloadAsync(IFileIo io, CancellationToken cancellationToken) =>
        _controller = new SaveController<JsonObject>(
            await ReadAsync(io, cancellationToken).ConfigureAwait(false));

    private static Task<JsonObject> ReadAsync(IFileIo io, CancellationToken cancellationToken) =>
        ProjectJson.LoadOrDefaultAsync(io, RelativePath, static () => new JsonObject(), cancellationToken);

    public IEnumerable<string> Dependencies => DependenciesObject()?.Select(x => x.Key) ?? [];

    public bool HasDependency(string name) => DependenciesObject()?.ContainsKey(name) ?? false;

    public void AddDependency(string name, Versioning.PackageVersion version)
    {
        var raw = _controller.AsMutable();

        if (raw["dependencies"] is not JsonObject dependencies)
        {
            dependencies = new JsonObject();
            raw["dependencies"] = dependencies;
        }

        dependencies[name] = version.ToString();
    }

    public void RemoveDependency(string name)
    {
        var raw = _controller.AsMutable();

        if (raw["dependencies"] is JsonObject dependencies)
        {
            dependencies.Remove(name);
        }
    }

    public Task SaveAsync(IFileIo io, CancellationToken cancellationToken) =>
        _controller.SaveAsync(
            raw => ProjectJson.SaveAsync(io, RelativePath, raw, cancellationToken));

    private JsonObject? DependenciesObject() => _controller.Value["dependencies"] as JsonObject;
}
