namespace VRCLearn.Core.Templates;

/// <summary>自家模板格式的扩展名、<c>$type</c> 与 id 命名空间。</summary>
public static class TemplateFormat
{
    public const string Extension = ".vrclearn-template";

    public const string Magic = "vpm.gamemode1.top.template";

    public const string Namespace = "vpm.gamemode1.top";

    /// <summary>ALCOM 那套，升级旧模板与文件关联都用到。</summary>
    public const string LegacyExtension = ".alcomtemplate";

    public const string LegacyMagic = "com.anatawa12.vrc-get.custom-template";

    public const string LegacyNamespace = "com.anatawa12.vrc-get";

    public static string IdOf(BuiltinTemplateKind kind) => $"{Namespace}.{Suffix(kind)}";

    public static string NewAnonymousId() => $"{Namespace}.user.{Guid.NewGuid():N}";

    private static string Suffix(BuiltinTemplateKind kind) => kind switch
    {
        BuiltinTemplateKind.Avatars => "vrchat.avatars",
        BuiltinTemplateKind.Worlds => "vrchat.worlds",
        _ => "blank",
    };
}
