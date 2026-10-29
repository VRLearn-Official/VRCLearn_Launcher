using System.Text;
using System.Text.Json.Nodes;
using VRCLearn.Core.Http;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Serialization;

namespace VRCLearn.Core.Repositories;

/// <summary>未知字段原样保留并写回。</summary>
public sealed class RemoteRepository
{
    private readonly JsonObject _raw;

    private RemoteRepository(
        JsonObject raw,
        string? name,
        Uri? url,
        string? id,
        Dictionary<string, RemotePackages> packages)
    {
        _raw = raw;
        Name = name;
        Url = url;
        Id = id;
        Packages = packages;
    }

    public string? Name { get; private set; }

    public Uri? Url { get; private set; }

    public string? Id { get; private set; }

    public IReadOnlyDictionary<string, RemotePackages> Packages { get; }

    public static RemoteRepository Parse(string json) =>
        JsonNode.Parse(json) is JsonObject raw
            ? Parse(raw)
            : throw new RepositoryParseException("repository must be a JSON object");

    public static RemoteRepository Parse(JsonObject raw) => new(
        raw,
        ReadString(raw, "name"),
        ReadUrl(raw, "url"),
        ReadString(raw, "id"),
        ReadPackages(raw));

    public static async Task<(RemoteRepository Repository, string? ETag)> DownloadAsync(
        IVpmHttpClient client,
        Uri url,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var result = await DownloadWithETagAsync(client, url, headers, null, cancellationToken).ConfigureAwait(false);

        return result ?? throw new InvalidOperationException("download without etag must return a result");
    }

    public static async Task<(RemoteRepository Repository, string? ETag)?> DownloadWithETagAsync(
        IVpmHttpClient client,
        Uri url,
        IReadOnlyDictionary<string, string> headers,
        string? currentETag,
        CancellationToken cancellationToken)
    {
        var fetch = await client.GetWithETagAsync(url, headers, currentETag, cancellationToken).ConfigureAwait(false);
        if (fetch is null)
        {
            return null;
        }

        await using var stream = fetch.Stream;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var repository = Parse(json);
        repository.SetUrlIfMissing(url);

        return (repository, fetch.ETag);
    }

    public void SetIdIfMissing(string id)
    {
        if (Id is not null)
        {
            return;
        }

        Id = id;
        _raw["id"] = id;
    }

    public void SetUrlIfMissing(Uri url)
    {
        var effective = Url ?? url;

        if (Url is null)
        {
            Url = effective;
            _raw["url"] = effective.AbsoluteUri;
        }

        SetIdIfMissing(effective.AbsoluteUri);
    }

    public RemotePackages? GetPackage(string package) => Packages.GetValueOrDefault(package);

    public IEnumerable<RemotePackages> GetPackages() => Packages.Values;

    public IEnumerable<PackageManifest> GetVersionsOf(string package) =>
        Packages.TryGetValue(package, out var packages) ? packages.AllVersions : [];

    public JsonObject ToRawJson() => (JsonObject)_raw.DeepClone();

    private static Dictionary<string, RemotePackages> ReadPackages(JsonObject raw)
    {
        var packages = new Dictionary<string, RemotePackages>();

        if (!raw.TryGetPropertyValue("packages", out var packagesNode))
        {
            return packages;
        }

        if (packagesNode is not JsonObject packagesObject)
        {
            throw new RepositoryParseException("packages must be an object");
        }

        foreach (var (name, value) in packagesObject)
        {
            packages[name] = value is JsonObject packageObject
                ? RemotePackages.Parse(packageObject)
                : throw new RepositoryParseException($"package {name} must be an object");
        }

        return packages;
    }

    private static string? ReadString(JsonObject raw, string key) =>
        raw.TryGetPropertyValue(key, out var node) ? JsonText.Of(node) : null;

    private static Uri? ReadUrl(JsonObject raw, string key)
    {
        var text = ReadString(raw, key);
        if (text is null)
        {
            return null;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            ? uri
            : throw new RepositoryParseException($"invalid url: {text}");
    }
}
