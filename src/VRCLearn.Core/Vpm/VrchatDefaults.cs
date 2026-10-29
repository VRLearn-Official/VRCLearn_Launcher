using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Vpm;

public static class VrchatDefaults
{
    public static UnityVersion Recommended2022Unity => UnityReleaseCatalog.StableVersion;

    /// <summary><see cref="Recommended2022Unity"/> 对应的 revision。</summary>
    public const string Recommended2022Revision = UnityReleaseCatalog.EditorHash;
}
