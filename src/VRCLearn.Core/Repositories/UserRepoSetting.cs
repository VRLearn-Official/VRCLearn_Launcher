using System.Text.Json.Serialization;

namespace VRCLearn.Core.Repositories;

public sealed class UserRepoSetting
{
    [JsonPropertyName("localPath")]
    public required string LocalPath { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("url")]
    public Uri? Url { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = [];

    public static UserRepoSetting Create(string localPath, string? name, Uri? url, string? id = null) => new()
    {
        LocalPath = localPath,
        Name = name,
        Url = url,
        Id = id ?? url?.AbsoluteUri,
        Headers = [],
    };
}
