using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Unity;

public static class UnityHubLink
{
    /// <summary>没有 revision 就返回 null。</summary>
    public static string? ForInstall(UnityVersion version, string? revision) =>
        string.IsNullOrWhiteSpace(revision) ? null : $"unityhub://{version}/{revision}";
}
