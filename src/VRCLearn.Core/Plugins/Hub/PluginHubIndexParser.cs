using System.Text.Json;
using System.Text.Json.Nodes;
using VRCLearn.Core.Serialization;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins.Hub;

public static class PluginHubIndexParser
{
    public const int MaxChars = 1024 * 1024;
    public const int MaxPlugins = 2000;
    public const int MaxVersionsPerPlugin = 256;

    public static bool TryParse(string json, out PluginHubIndex? index, out string error)
    {
        index = null;
        error = "";

        if (json.Length > MaxChars)
        {
            error = "index is too large";
            return false;
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }

        if (root is null)
        {
            error = "index is not a json object";
            return false;
        }

        if (root["schemaVersion"] is not JsonValue schemaValue
            || !schemaValue.TryGetValue<int>(out var schemaVersion)
            || schemaVersion != 1)
        {
            error = "unsupported schemaVersion";
            return false;
        }

        if (root["plugins"] is not JsonArray plugins)
        {
            error = "plugins is missing";
            return false;
        }

        if (plugins.Count > MaxPlugins)
        {
            error = "too many plugins";
            return false;
        }

        var entries = new List<PluginHubEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in plugins)
        {
            if (node is JsonObject plugin && ParseEntry(plugin) is { } entry && seen.Add(entry.Id))
            {
                entries.Add(entry);
            }
        }

        index = new PluginHubIndex { SchemaVersion = schemaVersion, Plugins = entries };

        return true;
    }

    private static PluginHubEntry? ParseEntry(JsonObject plugin)
    {
        var id = JsonText.Of(plugin["id"]);

        if (id is null || !PluginManifestValidator.IsValidId(id))
        {
            return null;
        }

        if (plugin["apiVersion"] is not JsonValue apiValue
            || !apiValue.TryGetValue<int>(out var apiVersion)
            || apiVersion <= 0)
        {
            return null;
        }

        PackageVersion? minHostVersion = null;

        if (JsonText.Of(plugin["minHostVersion"]) is { Length: > 0 } minHostText)
        {
            if (!PackageVersion.TryParse(minHostText, out var parsed))
            {
                return null;
            }

            minHostVersion = parsed;
        }

        if (plugin["versions"] is not JsonArray versions
            || versions.Count == 0
            || versions.Count > MaxVersionsPerPlugin)
        {
            return null;
        }

        var releases = new List<PluginHubRelease>();
        var seen = new HashSet<PackageVersion>();

        foreach (var node in versions)
        {
            if (node is JsonObject version && ParseRelease(version) is { } release && seen.Add(release.Version))
            {
                releases.Add(release);
            }
        }

        if (releases.Count == 0)
        {
            return null;
        }

        return new PluginHubEntry
        {
            Id = id,
            ApiVersion = apiVersion,
            MinHostVersion = minHostVersion,
            Versions = releases,
        };
    }

    private static PluginHubRelease? ParseRelease(JsonObject version)
    {
        var versionText = JsonText.Of(version["version"]);

        if (versionText is null || !PackageVersion.TryParse(versionText, out var parsed))
        {
            return null;
        }

        var url = JsonText.Of(version["url"]);

        if (url is null || url.Length > PluginHubEndpoint.MaxLength)
        {
            return null;
        }

        var sha256 = JsonText.Of(version["sha256"]);

        if (!string.IsNullOrEmpty(sha256) && !IsSha256(sha256))
        {
            return null;
        }

        if (version["size"] is not JsonValue sizeValue
            || !sizeValue.TryGetValue<long>(out var size)
            || size < 0)
        {
            return null;
        }

        DateTimeOffset? publishedAt = null;

        if (JsonText.Of(version["publishedAt"]) is { Length: > 0 } publishedText
            && DateTimeOffset.TryParse(publishedText, out var parsedDate))
        {
            publishedAt = parsedDate;
        }

        return new PluginHubRelease
        {
            Version = parsed,
            Size = size,
            PublishedAt = publishedAt,
            Url = url,
            Sha256 = string.IsNullOrEmpty(sha256) ? null : sha256.ToLowerInvariant(),
        };
    }

    private static bool IsSha256(string text) =>
        text.Length == 64 && text.All(Uri.IsHexDigit);
}
