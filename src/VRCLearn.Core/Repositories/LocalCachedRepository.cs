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

    private readonly JsonObject _extra = [];
    private readonly JsonObject _vrcGetExtra = [];

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

        foreach (var (key, value) in json)
        {
            if (key is not ("repo" or "headers" or "vrc-get"))
            {
                cached._extra[key] = value?.DeepClone();
            }
        }

        if (json["vrc-get"] is JsonObject meta)
        {
            foreach (var (key, value) in meta)
            {
                if (key == "etag")
                {
                    if (JsonText.Of(value) is { } etag)
                    {
                        cached.SetETag(etag);
                    }
                }
                else
                {
                    cached._vrcGetExtra[key] = value?.DeepClone();
                }
            }
        }

        return cached;
    }

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["repo"] = Repository.ToRawJson() };

        foreach (var (key, value) in _extra)
        {
            json[key] = value?.DeepClone();
        }

        if (Headers.Count > 0)
        {
            json["headers"] = new JsonObject(Headers.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Key, x.Value)));
        }

        var vrcGet = new JsonObject();

        foreach (var (key, value) in _vrcGetExtra)
        {
            vrcGet[key] = value?.DeepClone();
        }

        if (ETag is not null)
        {
            vrcGet["etag"] = ETag;
        }

        if (vrcGet.Count > 0)
        {
            json["vrc-get"] = vrcGet;
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

    /// <summary>刷新后的副本；只有落盘成功才替换缓存里的旧对象。</summary>
    public LocalCachedRepository WithRepository(RemoteRepository repository, string? etag)
    {
        var copy = new LocalCachedRepository(Repository, Headers);

        copy.SetRepository(repository);
        copy.SetETag(etag);

        foreach (var (key, value) in _extra)
        {
            copy._extra[key] = value?.DeepClone();
        }

        foreach (var (key, value) in _vrcGetExtra)
        {
            copy._vrcGetExtra[key] = value?.DeepClone();
        }

        return copy;
    }

    public IEnumerable<RemotePackages> GetPackages() => Repository.GetPackages();
}
