using Microsoft.Win32;
using VRCLearn.Core.Unity;

namespace VRCLearn.Infrastructure.Windows;

public enum SystemCheckKind
{
    UserName,
    DeviceName,
    FileExtensions,
    UnityHub,
    UnityEditor,
}

/// <param name="Detail">通过时通常是被检查的值，不通过时是有问题的字符；文件扩展名通过时为空串。</param>
public sealed record SystemCheckResult(SystemCheckKind Kind, bool Passed, string Detail);

public sealed class SystemCheckService(IUnityHubLauncher hub, IUnityEditorLocator editors)
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private const string HideFileExt = "HideFileExt";

    public static string NonAscii(string value) =>
        string.Concat(value.Where(x => x > 0x7F).Distinct());

    /// <summary>注册表里没有这个值时按系统的默认来：扩展名是藏着的。</summary>
    private static bool ExtensionsShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);

        return key?.GetValue(HideFileExt) is 0;
    }

    public Task<IReadOnlyList<SystemCheckResult>> RunAsync()
    {
        var found = editors.FindEditors();

        IReadOnlyList<SystemCheckResult> results =
        [
            Name(SystemCheckKind.UserName, ProfileName()),
            Name(SystemCheckKind.DeviceName, Environment.MachineName),
            new SystemCheckResult(SystemCheckKind.FileExtensions, ExtensionsShown(), ""),
            hub.ExecutablePath is { } hubPath
                ? new SystemCheckResult(SystemCheckKind.UnityHub, true, hubPath)
                : new SystemCheckResult(SystemCheckKind.UnityHub, false, ""),
            PreferredEditor(found) is { } editor
                ? new SystemCheckResult(SystemCheckKind.UnityEditor, true, editor.Version.ToString())
                : new SystemCheckResult(SystemCheckKind.UnityEditor, false, ""),
        ];

        return Task.FromResult(results);
    }

    private static UnityEditor? PreferredEditor(IReadOnlyList<UnityEditor> editors) =>
        editors.FirstOrDefault(x => x.Version == UnityReleaseCatalog.StableVersion)
        ?? editors.FirstOrDefault(x => UnityReleaseCatalog.SupportedVersions.Contains(x.Version));

    private static SystemCheckResult Name(SystemCheckKind kind, string value) =>
        NonAscii(value) is { Length: > 0 } bad
            ? new SystemCheckResult(kind, false, bad)
            : new SystemCheckResult(kind, true, value);

    private static string ProfileName()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.GetFileName(profile) is { Length: > 0 } name ? name : Environment.UserName;
    }
}
