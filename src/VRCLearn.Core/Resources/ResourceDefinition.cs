using System.Text.Json.Serialization;

namespace VRCLearn.Core.Resources;

public sealed class ResourceDefinition : IJsonOnDeserialized
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>相对资源目录的封面路径，例如 <c>covers/{id}.png</c>。</summary>
    public string CoverPath { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    /// <summary>说明书的本地文档绝对路径或网址。</summary>
    public string Manual { get; set; } = "";

    /// <summary>源 unitypackage 的绝对路径，按顺序导入。</summary>
    public List<string> UnityPackagePaths { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public void OnDeserialized()
    {
        Id ??= "";
        Name ??= "";
        CoverPath ??= "";
        Manual ??= "";
        Tags ??= [];
        UnityPackagePaths ??= [];
    }
}
