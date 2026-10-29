using VRCLearn.Core.IO;
using VRCLearn.Core.Templates;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Infrastructure.ProjectTemplates;

public sealed class FileSystemBuiltinTemplateStore(IFileIoFactory fileIo) : IBuiltinTemplateStore
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "Templates");

    private IReadOnlyList<BuiltinTemplate>? _templates;

    public IReadOnlyList<BuiltinTemplate> Templates => _templates ??=
    [
        new(BuiltinTemplateKind.Avatars, Installed(BuiltinTemplateKind.Avatars)),
        new(BuiltinTemplateKind.Worlds, Installed(BuiltinTemplateKind.Worlds)),

        new(BuiltinTemplateKind.Blank, []),
    ];

    public IFileIo? Open(BuiltinTemplateKind kind, UnityVersion unityVersion)
    {
        if (kind == BuiltinTemplateKind.Blank)
        {
            return null;
        }

        var directory = Path.Combine(_root, $"{FolderName(kind)}-{unityVersion}");

        return Directory.Exists(directory) ? fileIo.For(directory) : null;
    }

    private static string FolderName(BuiltinTemplateKind kind) =>
        kind == BuiltinTemplateKind.Avatars ? "avatars" : "worlds";

    private IReadOnlyList<UnityVersion> Installed(BuiltinTemplateKind kind)
    {
        var version = UnityReleaseCatalog.StableVersion;

        return Directory.Exists(Path.Combine(_root, $"{FolderName(kind)}-{version}")) ? [version] : [];
    }
}
