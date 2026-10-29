using System.Text.Json.Nodes;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Serialization;

namespace VRCLearn.Core.Repositories;

/// <summary>落盘格式：<c>{ "repo": {...}, "headers": {...}, "vrc-get": { "etag": ... } }</c>。</summary>
public sealed class LocalCachedRepository
{
    public LocalCachedRepository(RemoteRepository repository, IReadOnlyDictionary<string, string> headers)
    {
        Repository = repository;
        Headers = headers;
    }

    public RemoteRepository Repository { get; private set; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public string? ETag { get; private set; }

    public Uri? Url => Repository.Url;

    public string? Id => Repository.Id;

    public string? Name => Repository.Name;

    public static LocalCachedRepository FromJson(JsonObject json)
    {
        if (json["repo"] is not JsonObject repoNode)
        {
            throw new RepositoryParseException("repository cache must contain a repo object");
        }

        var headers = new Dictionary<string, string>();
        if (json["headers"] is JsonObject headersNode)
        {
            foreach (var (key, value) in headersNode)
            {
                if (JsonText.Of(value) is { } text)
                {
                    headers[key] = text;
                }
            }
        }

        var cached = new LocalCachedRepository(RemoteRepository.Parse(repoNode), headers);

        if (json["vrc-get"] is JsonObject meta && JsonText.Of(meta["etag"]) is { } etag)
        {
            cached.SetETag(etag);
        }

        return cached;
    }

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["repo"] = Repository.ToRawJson() };

        if (Headers.Count > 0)
        {
            json["headers"] = new JsonObject(Headers.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Key, x.Value)));
        }

        if (ETag is not null)
        {
            json["vrc-get"] = new JsonObject { ["etag"] = ETag };
        }

        return json;
    }

    public void SetRepository(RemoteRepository repository)
    {
        if (Id is { } id)
        {
            repository.SetIdIfMissing(id);
        }

        if (Url is { } url)
        {
            repository.SetUrlIfMissing(url);
        }

        Repository = repository;
    }

    public void SetETag(string? etag) => ETag = string.IsNullOrEmpty(etag) ? null : etag;

    public IEnumerable<RemotePackages> GetPackages() => Repository.GetPackages();
}
