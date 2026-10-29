namespace VRCLearn.Core.Projects;

/// <summary>现役的排前面，老式的排后面，最末尾是 LegacySdk2。</summary>
public static class ProjectTypeRank
{
    public static int Of(ProjectType type) => type switch
    {
        ProjectType.Avatars => 1,
        ProjectType.Worlds => 2,
        ProjectType.VpmStarter => 3,
        ProjectType.Unknown => 4,
        ProjectType.UpmAvatars => 5,
        ProjectType.LegacyAvatars => 6,
        ProjectType.UpmWorlds => 7,
        ProjectType.LegacyWorlds => 8,
        ProjectType.UpmStarter => 9,
        ProjectType.LegacySdk2 => 10,

        _ => 11,
    };
}
