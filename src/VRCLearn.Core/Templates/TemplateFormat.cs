namespace VRCLearn.Core.Templates;

/// <summary>模板格式的扩展名、<c>$type</c> 与空白模板 id。</summary>
public static class TemplateFormat
{
    public const string Extension = ".vrclearn-template";

    public const string Magic = "vrclearn.template";

    public const string BlankId = "blank";

    /// <summary>ALCOM 那套，导入旧模板时用到。</summary>
    public const string LegacyExtension = ".alcomtemplate";

    public const string LegacyMagic = "com.anatawa12.vrc-get.custom-template";

    public const string LegacyNamespace = "com.anatawa12.vrc-get";

    public static string NewAnonymousId() => Guid.NewGuid().ToString("N");
}
