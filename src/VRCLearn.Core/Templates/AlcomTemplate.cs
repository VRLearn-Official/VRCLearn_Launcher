using System.Text.Json;
using System.Text.Json.Nodes;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Serialization;

namespace VRCLearn.Core.Templates;

/// <summary><c>.vrclearn-template</c>：以某个模板为底，叠加 vpm 依赖和 <c>.unitypackage</c>。</summary>
public sealed record AlcomTemplate(
    string DisplayName,
    string? Id,
    string Base,
    string? UnityVersion,
    IReadOnlyDictionary<string, string> VpmDependencies,
    IReadOnlyList<string> UnityPackages,
    DateTimeOffset? UpdatedAt,
    string? Files = null)
{
    public static AlcomTemplate? TryParse(string content)
    {
        var json = content.Split('\0')[0];

        try
        {
            return JsonNode.Parse(json) is JsonObject root ? FromJson(root) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AlcomTemplate? FromJson(JsonObject json)
    {
        if (JsonText.Of(json["$type"]) != TemplateFormat.Magic
            || JsonText.Of(json["base"]) is not { Length: > 0 } baseId
            || JsonText.Of(json["displayName"]) is not { Length: > 0 } displayName)
        {
            return null;
        }

        var dependencies = new Dictionary<string, string>();

        if (json["vpmDependencies"] is JsonObject deps)
        {
            foreach (var (name, value) in deps)
            {
                if (JsonText.Of(value) is { Length: > 0 } range)
                {
                    dependencies[name] = range;
                }
            }
        }

        var packages = new List<string>();

        if (json["unityPackages"] is JsonArray array)
        {
            packages.AddRange(array.Select(JsonText.Of).OfType<string>());
        }

        return new AlcomTemplate(
            displayName,
            JsonText.Of(json["id"]),
            baseId,
            JsonText.Of(json["unityVersion"]),
            dependencies,
            packages,
            ParseDate(JsonText.Of(json["updateDate"])),
            JsonText.Of(json["files"]) is { Length: > 0 } files ? files : null);
    }

    private static DateTimeOffset? ParseDate(string? text) =>
        DateTimeOffset.TryParse(text, out var value) ? value : null;

    public string ToJson() => ToNode().ToJsonString(JsonDefaults.Indented);

    private JsonObject ToNode()
    {
        var json = new JsonObject
        {
            ["$type"] = TemplateFormat.Magic,
            ["formatVersion"] = FormatVersion,
            ["displayName"] = DisplayName,
            ["id"] = Id,
            ["base"] = Base,
            ["unityVersion"] = UnityVersion,
        };

        if (UpdatedAt is { } updated)
        {
            json["updateDate"] = updated.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        if (Files is { Length: > 0 } files)
        {
            json["files"] = files;
        }

        if (VpmDependencies.Count > 0)
        {
            json["vpmDependencies"] = new JsonObject(
                VpmDependencies.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Key, x.Value)));
        }

        if (UnityPackages.Count > 0)
        {
            json["unityPackages"] = new JsonArray([.. UnityPackages.Select(x => JsonValue.Create(x))]);
        }

        return json;
    }

    private const string FormatVersion = "1.0";
}
