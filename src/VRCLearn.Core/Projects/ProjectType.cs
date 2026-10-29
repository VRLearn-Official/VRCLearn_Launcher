namespace VRCLearn.Core.Projects;

public enum ProjectType
{
    Unknown,

    LegacySdk2,

    LegacyWorlds,
    LegacyAvatars,

    /// <summary>用 UPM 装的 VRCSDK，没走 VPM。</summary>
    UpmWorlds,
    UpmAvatars,
    UpmStarter,

    Worlds,
    Avatars,

    /// <summary>有 VPM 清单，但不是 VRCSDK 项目。</summary>
    VpmStarter,
}
