using VRCLearn.Core.IO;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Templates;

public enum BuiltinTemplateKind
{
    Avatars,

    Worlds,

    /// <summary>空白项目：三个空目录加一个 <c>ProjectVersion.txt</c>。</summary>
    Blank,
}

/// <param name="UnityVersions">空白模板是空的，表示对版本没要求。</param>
public sealed record BuiltinTemplate(
    BuiltinTemplateKind Kind,
    IReadOnlyList<UnityVersion> UnityVersions);

public interface IBuiltinTemplateStore
{
    IReadOnlyList<BuiltinTemplate> Templates { get; }

    /// <summary>空白模板没有文件；版本对不上时返回 null。</summary>
    IFileIo? Open(BuiltinTemplateKind kind, UnityVersion unityVersion);
}
