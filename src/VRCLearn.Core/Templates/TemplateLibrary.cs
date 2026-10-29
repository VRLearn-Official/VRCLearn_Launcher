using VRCLearn.Core.IO;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Templates;

public sealed record ProjectTemplate(
    string Id,
    string DisplayName,
    IReadOnlyList<UnityVersion> UnityVersions,
    bool IsAvailable,
    string? SourceFile = null,
    AlcomTemplate? Definition = null);

/// <param name="Source">内容从哪复制。<c>null</c> 表示空白项目。</param>
public sealed record TemplatePlan(
    IFileIo? Source,
    IReadOnlyDictionary<string, DependencyRange> Dependencies,
    IReadOnlyList<string> UnityPackages);

public sealed class TemplateLibrary
{
    private readonly IReadOnlyDictionary<string, ProjectTemplate> _byId;
    private readonly IReadOnlyList<string> _order;
    private readonly IReadOnlyDictionary<string, AlcomTemplate> _alcom;
    private readonly IFileIoFactory _fileIo;

    private TemplateLibrary(
        IReadOnlyDictionary<string, ProjectTemplate> byId,
        IReadOnlyList<string> order,
        IReadOnlyDictionary<string, AlcomTemplate> alcom,
        IFileIoFactory fileIo)
    {
        _byId = byId;
        _order = order;
        _alcom = alcom;
        _fileIo = fileIo;
    }

    /// <summary>空白项目排前面，然后是按名字排的 <c>.vrclearn-template</c>。</summary>
    public IReadOnlyList<ProjectTemplate> Templates => [.. _order.Select(x => _byId[x])];

    public static TemplateLibrary Build(
        IReadOnlyList<AlcomTemplateFile> alcomFiles,
        IReadOnlyList<UnityVersion> installed,
        IFileIoFactory fileIo)
    {
        var byId = new Dictionary<string, ProjectTemplate>
        {
            [TemplateFormat.BlankId] = new(TemplateFormat.BlankId, "Blank", installed, true),
        };

        var order = new List<string> { TemplateFormat.BlankId };
        var alcomById = new Dictionary<string, AlcomTemplate>();

        foreach (var file in alcomFiles.OrderBy(x => x.Template.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var id = file.Template.Id ?? TemplateFormat.NewAnonymousId();

            alcomById[id] = file.Template;

            byId[id] = new ProjectTemplate(
                id,
                file.Template.DisplayName,
                [],
                false,
                file.Path,
                file.Template);

            order.Add(id);
        }

        ResolveBases(byId, alcomById);

        return new TemplateLibrary(byId, order, alcomById, fileIo);
    }

    private static void ResolveBases(
        Dictionary<string, ProjectTemplate> byId,
        IReadOnlyDictionary<string, AlcomTemplate> alcom)
    {
        var pending = alcom.Keys.ToHashSet();

        while (pending.Count > 0)
        {
            var progressed = false;

            foreach (var id in pending.ToList())
            {
                if (!byId.TryGetValue(alcom[id].Base, out var baseTemplate) || !baseTemplate.IsAvailable)
                {
                    continue;
                }

                byId[id] = byId[id] with
                {
                    UnityVersions = FilterVersions(baseTemplate.UnityVersions, alcom[id].UnityVersion),
                    IsAvailable = true,
                };

                pending.Remove(id);
                progressed = true;
            }

            if (!progressed)
            {
                return;
            }
        }
    }

    private static IReadOnlyList<UnityVersion> FilterVersions(
        IReadOnlyList<UnityVersion> versions,
        string? filter) =>
        filter is null || !VersionRange.TryParse(filter, out var range)
            ? versions
            : [.. versions.Where(x => range.Matches(AsPackageVersion(x)))];

    private static PackageVersion AsPackageVersion(UnityVersion version) =>
        PackageVersion.Parse($"{version.Major}.{version.Minor}.{version.Revision}");

    /// <summary>模板用不了、或者版本对不上时返回 null。</summary>
    public TemplatePlan? Plan(string id, UnityVersion unityVersion)
    {
        if (!_byId.TryGetValue(id, out var template)
            || !template.IsAvailable
            || !template.UnityVersions.Contains(unityVersion))
        {
            return null;
        }

        var dependencies = new Dictionary<string, DependencyRange>();
        var packages = new List<string>();

        Resolve(id, dependencies, packages);

        return new TemplatePlan(SourceOf(template), dependencies, packages);
    }

    private IFileIo? SourceOf(ProjectTemplate template)
    {
        if (template.Definition?.Files is not { Length: > 0 } files || template.SourceFile is null)
        {
            return null;
        }

        var directory = Path.GetFullPath(Path.GetDirectoryName(template.SourceFile)!);
        var path = Path.GetFullPath(Path.Combine(directory, files));

        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(path))
        {
            return null;
        }

        return _fileIo.For(path);
    }

    private void Resolve(
        string id,
        Dictionary<string, DependencyRange> dependencies,
        List<string> packages)
    {
        if (id == TemplateFormat.BlankId || !_alcom.TryGetValue(id, out var alcom))
        {
            return;
        }

        Resolve(alcom.Base, dependencies, packages);

        foreach (var (name, text) in alcom.VpmDependencies)
        {
            var range = DependencyRange.Parse(text);

            dependencies[name] = dependencies.TryGetValue(name, out var existing)
                ? range.Intersect(existing)
                : range;
        }

        packages.AddRange(alcom.UnityPackages);
    }
}
