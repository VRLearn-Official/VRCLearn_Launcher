using System.Text.Json;
using System.Text.Json.Nodes;
using VRCLearn.Core.Serialization;

namespace VRCLearn.Core.Unity;

/// <summary>Hub 的私有格式，只读不写，读不出来就当没有。</summary>
public static class UnityHubLocalSettings
{
    /// <summary>读 <c>secondaryInstallPath.json</c>，内容就是一个 JSON 字符串。</summary>
    public static string? ParseSecondaryInstallPath(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var path = JsonNode.Parse(json)?.GetValue<string>();

            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>从 Hub 的 <c>settings.json</c> 或全局 <c>services-config.json</c> 里读第二安装位置。</summary>
    public static string? ParseMachineWideInstallLocation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var path = JsonNode.Parse(json)?["machineWideSecondaryInstallLocation"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>读 <c>editors-v2.json</c>，里面的路径可能已不存在。</summary>
    public static IReadOnlyList<UnityEditor> ParseEditors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json)?["data"] is not JsonArray editors
                ? []
                : [.. editors.SelectMany(ParseEditor)];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static IEnumerable<UnityEditor> ParseEditor(JsonNode? node)
    {
        if (node is not JsonObject editor
            || Versioning.UnityVersion.TryParse(JsonText.Of(editor["version"])) is not { } version)
        {
            return [];
        }

        var architecture = ReadArchitecture(JsonText.Of(editor["architecture"]));

        var locations = editor["location"] switch
        {
            JsonArray array => array.Select(JsonText.Of).OfType<string>(),
            var single => JsonText.Of(single) is { } path ? [path] : [],
        };

        return locations.Select(x => new UnityEditor(version, x, architecture));
    }

    private static ChipArchitecture? ReadArchitecture(string? text) => text switch
    {
        "x86_64" or null => ChipArchitecture.X86_64,
        "arm64" => ChipArchitecture.Arm64,
        _ => null,
    };
}
