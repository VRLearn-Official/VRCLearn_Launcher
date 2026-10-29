using System.Text.Json.Nodes;

namespace VRCLearn.Core.Serialization;

/// <summary>读 JSON 里的字符串，值不是字符串时当没写，而不是抛 <see cref="InvalidOperationException"/>。</summary>
internal static class JsonText
{
    public static string? Of(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
