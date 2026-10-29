using System.Text.Json;
using System.Text.Json.Nodes;
using VRCLearn.Core.Serialization;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Packages;

/// <summary>可选字段解析失败时只丢该字段，name 与 version 仍必需。</summary>
internal static class LoosePackageManifest
{
    public static PackageManifest? TryRead(JsonObject json)
    {
        var cleaned = new JsonObject();

        foreach (var (key, node) in json)
        {
            if (IsParsable(key, node))
            {
                cleaned[key] = node?.DeepClone();
            }
        }

        try
        {
            return cleaned.Deserialize<PackageManifest>(VpmJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsParsable(string key, JsonNode? node) => key switch
    {
        "displayName" or "description" or "zipSHA256" => Parses<string>(node),
        "unity" => Parses<PartialUnityVersion>(node),
        "url" or "changelogUrl" or "documentationUrl" => Parses<Uri>(node),
        "vpmDependencies" => Parses<OrderedDictionary<string, VersionRange>>(node),
        "legacyFolders" or "legacyFiles" => Parses<OrderedDictionary<string, string?>>(node),
        "legacyPackages" or "keywords" => Parses<List<string>>(node),
        "headers" => Parses<OrderedDictionary<string, string>>(node),
        "vrc-get" => Parses<VrcGetMeta>(node),

        _ => true,
    };

    private static bool Parses<T>(JsonNode? node)
    {
        try
        {
            _ = node.Deserialize<T>(VpmJson.Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
