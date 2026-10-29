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
    private readonly IReadOnlyDictionary<string, BuiltinTemplateKind> _builtinKinds;
    private readonly IBuiltinTemplateStore _builtin;

    private TemplateLibrary(
        IReadOnlyDictionary<string, ProjectTemplate> byId,
        IReadOnlyList<string> order,
        IReadOnlyDictionary<string, AlcomTemplate> alcom,
        IReadOnlyDictionary<string, BuiltinTemplateKind> builtinKinds,
        IBuiltinTemplateStore builtin)
    {
        _byId = byId;
        _order = order;
        _alcom = alcom;
        _builtinKinds = builtinKinds;
        _builtin = builtin;
    }

    /// <summary>内置三个排前面，然后是按名字排的 <c>.vrclearn-template</c>。</summary>
    public IReadOnlyList<ProjectTemplate> Templates => [.. _order.Select(x => _byId[x])];

    public static TemplateLibrary Build(
        IBuiltinTemplateStore builtinStore,
        IReadOnlyList<AlcomTemplateFile> alcomFiles,
        IReadOnlyList<UnityVersion> installed)
    {
        var byId = new Dictionary<string, ProjectTemplate>();
        var alcomById = new Dictionary<string, AlcomTemplate>();
        var builtinKinds = new Dictionary<string, BuiltinTemplateKind>();
        var order = new List<string>();

        foreach (var template in builtinStore.Templates)
        {
            var id = TemplateFormat.IdOf(template.Kind);

            byId[id] = new ProjectTemplate(
                id,
                template.Kind.ToString(),
                template.Kind == BuiltinTemplateKind.Blank ? installed : template.UnityVersions,
                true);

            order.Add(id);
            builtinKinds[id] = template.Kind;
        }

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

        return new TemplateLibrary(byId, order, alcomById, builtinKinds, builtinStore);
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

        var builtin = PlanInto(id, dependencies, packages);

        return new TemplatePlan(_builtin.Open(builtin, unityVersion), dependencies, packages);
    }

    private BuiltinTemplateKind PlanInto(
        string id,
        Dictionary<string, DependencyRange> dependencies,
        List<string> packages)
    {
        if (!_alcom.TryGetValue(id, out var alcom))
        {
            return _builtinKinds[id];
        }

        var builtin = PlanInto(alcom.Base, dependencies, packages);

        foreach (var (name, text) in alcom.VpmDependencies)
        {
            var range = DependencyRange.Parse(text);

            dependencies[name] = dependencies.TryGetValue(name, out var existing)
                ? range.Intersect(existing)
                : range;
        }

        packages.AddRange(alcom.UnityPackages);

        return builtin;
    }
}
