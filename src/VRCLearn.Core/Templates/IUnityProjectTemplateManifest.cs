using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Templates;

public interface IUnityProjectTemplateManifest
{
    Task<byte[]?> FindAsync(
        UnityVersion unityVersion,
        string? editorExecutablePath,
        CancellationToken cancellationToken);
}
