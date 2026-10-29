using System.Text.Json.Serialization;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Packages;

/// <summary>反序列化时缺失或为 null 的集合字段归一化成空集合。</summary>
public sealed class PackageManifest : IJsonOnDeserialized
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required PackageVersion Version { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("unity")]
    public PartialUnityVersion? Unity { get; init; }

    [JsonPropertyName("url")]
    public Uri? Url { get; init; }

    [JsonPropertyName("zipSHA256")]
    public string? ZipSha256 { get; init; }

    [JsonPropertyName("vpmDependencies")]
    public OrderedDictionary<string, VersionRange> VpmDependencies { get; set; } = [];

    [JsonPropertyName("legacyFolders")]
    public OrderedDictionary<string, string?> LegacyFolders { get; set; } = [];

    [JsonPropertyName("legacyFiles")]
    public OrderedDictionary<string, string?> LegacyFiles { get; set; } = [];

    [JsonPropertyName("legacyPackages")]
    public List<string> LegacyPackages { get; set; } = [];

    [JsonPropertyName("headers")]
    public OrderedDictionary<string, string> Headers { get; set; } = [];

    [JsonPropertyName("changelogUrl")]
    public Uri? ChangelogUrl { get; init; }

    [JsonPropertyName("documentationUrl")]
    public Uri? DocumentationUrl { get; init; }

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];

    [JsonPropertyName("vrc-get")]
    public VrcGetMeta VrcGet { get; set; } = new();

    [JsonIgnore]
    public bool IsYanked => VrcGet.Yanked.IsYanked;

    [JsonIgnore]
    public IReadOnlyList<string> Aliases => VrcGet.Aliases;

    void IJsonOnDeserialized.OnDeserialized()
    {
        VpmDependencies ??= [];
        LegacyFolders ??= [];
        LegacyFiles ??= [];
        LegacyPackages ??= [];
        Headers ??= [];
        Keywords ??= [];
        VrcGet ??= new VrcGetMeta();
    }
}

public sealed class VrcGetMeta : IJsonOnDeserialized
{
    [JsonPropertyName("yanked")]
    public YankState Yanked { get; set; } = YankState.NotYanked;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    void IJsonOnDeserialized.OnDeserialized()
    {
        Yanked ??= YankState.NotYanked;
        Aliases ??= [];
    }
}
