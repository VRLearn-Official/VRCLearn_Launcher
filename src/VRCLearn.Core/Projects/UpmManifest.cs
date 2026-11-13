using VRCLearn.Core.IO;
using System.Text.Json.Nodes;

namespace VRCLearn.Core.Projects;

/// <summary><c>Packages/manifest.json</c> 的只读视图。</summary>
public sealed class UpmManifest
{
    public const string RelativePath = "Packages/manifest.json";

    private JsonObject _raw;

    private UpmManifest(JsonObject raw) => _raw = raw;

    public static async Task<UpmManifest> LoadAsync(IFileIo io, CancellationToken cancellationToken) =>
        new(await ReadAsync(io, cancellationToken).ConfigureAwait(false));

    public async Task ReloadAsync(IFileIo io, CancellationToken cancellationToken) =>
        _raw = await ReadAsync(io, cancellationToken).ConfigureAwait(false);

    private static Task<JsonObject> ReadAsync(IFileIo io, CancellationToken cancellationToken) =>
        ProjectJson.LoadOrDefaultAsync(io, RelativePath, static () => new JsonObject(), cancellationToken);

    public bool HasDependency(string name) =>
        _raw["dependencies"] is JsonObject dependencies && dependencies.ContainsKey(name);
}
