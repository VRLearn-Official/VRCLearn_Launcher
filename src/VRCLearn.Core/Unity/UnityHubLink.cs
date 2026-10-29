using VRCLearn.Core.Versioning;
using VRCLearn.Core.Vpm;

namespace VRCLearn.Core.Unity;

public static class UnityHubLink
{
    /// <summary>没有 revision 就返回 null。</summary>
    public static string? ForInstall(UnityVersion version, string? revision) =>
        string.IsNullOrWhiteSpace(revision) ? null : $"unityhub://{version}/{revision}";

    public static string Recommended2022Install =>
        ForInstall(VrchatDefaults.Recommended2022Unity, VrchatDefaults.Recommended2022Revision)!;
}
