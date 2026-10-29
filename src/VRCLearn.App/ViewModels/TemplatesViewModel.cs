using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Collections;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Templates;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class TemplatesViewModel : PageViewModelBase
{
    private readonly IBuiltinTemplateStore _templates;
    private readonly IUserTemplateStore _userTemplates;
    private readonly IUnityEditorLocator _editors;
    private readonly RepositoryService _repositories;
    private readonly UserPackageService _userPackages;
    private readonly LocalizationService _localization;
    private readonly GuiConfigStore _configStore;
    private readonly DialogService _dialogs;
    private readonly ILogger<TemplatesViewModel> _logger;

    private TemplateLibrary _library;
    private PackageCatalog _catalog = PackageCatalog.FromRepositories([]);

    public TemplatesViewModel(
        IBuiltinTemplateStore templates,
        IUserTemplateStore userTemplates,
        IUnityEditorLocator editors,
        RepositoryService repositories,
        UserPackageService userPackages,
        LocalizationService localization,
        GuiConfigStore configStore,
        DialogService dialogs,
        ToastService toasts,
        ILogger<TemplatesViewModel> logger)
        : base(toasts)
    {
        _logger = logger;
        _configStore = configStore;
        _templates = templates;
        _userTemplates = userTemplates;
        _editors = editors;
        _repositories = repositories;
        _userPackages = userPackages;
        _localization = localization;

        _localization.PropertyChanged += (_, _) => _ = ReloadAsync();
        _dialogs = dialogs;

        _library = TemplateLibrary.Build(templates, [], []);
    }

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public ObservableCollection<TemplateOption> Templates { get; } = [];

    public string FileExtension => TemplateFormat.Extension;

    public async Task ReloadAsync()
    {
        var user = await _userTemplates.LoadAsync(CancellationToken.None);

        _library = TemplateLibrary.Build(
            _templates,
            user.AlcomTemplates,
            [.. _editors
                .FindEditors()
                .Select(x => x.Version)
                .Where(x => UnityReleaseCatalog.SupportedVersions.Contains(x))]);

        var favorites = _configStore.Current.FavoriteTemplates;

        Templates.Clear();

        foreach (var template in _library.Templates.OrderByDescending(x => favorites.Contains(x.Id)))
        {
            Templates.Add(new TemplateOption(template, favorites.Contains(template.Id), _localization));
        }
    }

    public TemplatePlan? PlanFor(string id, UnityVersion unityVersion) => _library.Plan(id, unityVersion);

    [RelayCommand]
    private void CreateFrom(TemplateOption option) => _dialogs.ShowNewProject(option);

    // ---- 编辑模板 ----

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editorTitle = "";

    [ObservableProperty]
    private string _editorName = "";

    public ObservableCollection<TemplateOption> Bases { get; } = [];

    [ObservableProperty]
    private TemplateOption? _editorBase;

    /// <summary>形如 <c>2022.x.x</c>；留空跟随 base。</summary>
    [ObservableProperty]
    private string _editorUnityVersion = "";

    public ObservableCollection<string> UnityVersions { get; } = [];

    public ObservableCollection<string> PackageNames { get; } = [];

    public ObservableCollection<TemplateDependencyViewModel> EditorDependencies { get; } = [];

    public ObservableCollection<string> EditorUnityPackages { get; } = [];

    private string? _editingId;

    [RelayCommand]
    private Task NewTemplateAsync() => OpenEditorAsync(_localization["Templates.Editor.New"], null, null);

    [RelayCommand]
    private Task EditTemplateAsync(TemplateOption option) =>
        OpenEditorAsync(_localization.Format("Templates.Editor.Edit", option.Display), option, option.Id);

    [RelayCommand]
    private async Task DuplicateTemplateAsync(TemplateOption option)
    {
        await OpenEditorAsync(_localization.Format("Templates.Editor.Duplicate", option.Display), option, null);

        EditorName = _localization.Format("Templates.Editor.CopyName", option.Display);
    }

    private async Task OpenEditorAsync(string title, TemplateOption? source, string? id)
    {
        _editingId = id;
        EditorTitle = title;
        EditorName = source?.Display ?? "";
        EditorUnityVersion = "";
        EditorDependencies.Clear();
        EditorUnityPackages.Clear();

        UnityVersions.Clear();
        foreach (var version in _editors.FindEditors().OrderByDescending(x => x.Version))
        {
            UnityVersions.Add(version.Version.ToString());
        }

        _catalog = PackageCatalog.FromRepositories(
            _repositories.CachedRepositories(),
            await _userPackages.LoadPackagesAsync(CancellationToken.None));

        PackageNames.Clear();
        foreach (var name in _catalog.PackageNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            PackageNames.Add(name);
        }

        Bases.Clear();

        foreach (var template in Templates.Where(x => x.IsAvailable))
        {
            Bases.Add(template);
        }

        if (source?.Definition is { } definition)
        {
            EditorBase = Bases.FirstOrDefault(x => x.Id == definition.Base);

            foreach (var (name, range) in definition.VpmDependencies)
            {
                EditorDependencies.Add(new TemplateDependencyViewModel(name, range, _catalog));
            }

            foreach (var package in definition.UnityPackages)
            {
                EditorUnityPackages.Add(package);
            }

            EditorUnityVersion = definition.UnityVersion ?? "";
        }

        EditorBase ??= Bases.FirstOrDefault();
        IsEditing = true;
    }

    [RelayCommand]
    private void AddDependency()
    {
        EditorDependencies.Add(new TemplateDependencyViewModel("", "", _catalog));
    }

    [RelayCommand]
    private void RemoveDependency(TemplateDependencyViewModel dependency)
    {
        EditorDependencies.Remove(dependency);
    }

    public void AddUnityPackages(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(x => !EditorUnityPackages.Contains(x)))
        {
            EditorUnityPackages.Add(path);
        }
    }

    [RelayCommand]
    private void RemoveUnityPackage(string path)
    {
        EditorUnityPackages.Remove(path);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveTemplate()
    {
        if (EditorBase is not { } editorBase)
        {
            Status = _localization["Templates.Editor.NoBase"];
            return;
        }

        var name = EditorName.Trim();

        if (name.Length == 0)
        {
            Status = _localization["Templates.Editor.NameEmpty"];
            return;
        }

        var dependencies = new Dictionary<string, string>();

        foreach (var dependency in EditorDependencies)
        {
            var package = dependency.Package.Trim();

            if (package.Length == 0)
            {
                continue;
            }

            if (!VersionRange.TryParse(dependency.Range.Trim(), out _))
            {
                Status = _localization.Format("Templates.Editor.BadRange", dependency.Range, package);
                return;
            }

            dependencies[package] = dependency.Range.Trim();
        }

        if (EditorUnityVersion.Trim().Length > 0 && !VersionRange.TryParse(EditorUnityVersion.Trim(), out _))
        {
            Status = _localization.Format("Templates.Editor.BadRange", EditorUnityVersion, "unityVersion");
            return;
        }

        try
        {
            await _userTemplates.SaveAsync(
                new AlcomTemplate(
                    name,
                    _editingId ?? TemplateFormat.NewAnonymousId(),
                    editorBase.Id,
                    EditorUnityVersion.Trim().Length > 0 ? EditorUnityVersion.Trim() : null,
                    dependencies,
                    [.. EditorUnityPackages],
                    DateTimeOffset.Now),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "保存模板失败：{Template}", name);

            Status = _localization.Format("Templates.Status.SaveFailed", exception.Message);

            return;
        }

        IsEditing = false;
        Status = _localization.Format("Templates.Status.Saved", name);

        await ReloadAsync();
    }

    [RelayCommand]
    private void ToggleFavorite(TemplateOption option)
    {
        var removed = _configStore.Current.FavoriteTemplates.Toggle(option.Id);

        _logger.LogInformation("{Action}模板：{Template}", removed ? "取消收藏" : "收藏", option.Id);

        _configStore.Save();
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void Remove(TemplateOption option)
    {
        if (option.SourceFile is not { } path)
        {
            return;
        }

        if (!_dialogs.ShowConfirm(
                _localization["Templates.Remove.ConfirmTitle"],
                _localization.Format("Templates.Remove.ConfirmMessage", option.Display),
                _localization["Templates.Remove"],
                danger: true))
        {
            return;
        }

        Status = _userTemplates.Remove(path)
            ? _localization.Format("Templates.Status.Removed", option.Display)
            : _localization["Templates.Status.Gone"];

        _ = ReloadAsync();
    }

    public string? Export(TemplateOption option) => option.SourceFile is { } path && File.Exists(path)
        ? File.ReadAllText(path)
        : null;

    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        var result = await _userTemplates.ImportAsync(paths, CancellationToken.None);

        PendingImport.Clear();

        foreach (var duplicate in result.Duplicates)
        {
            PendingImport.Add(new TemplateImportViewModel(duplicate, _localization));
        }

        OnPropertyChanged(nameof(HasPendingImport));

        Status = result.Duplicates.Count == 0
            ? _localization.Format("Templates.Status.Imported", result.Imported)
            : _localization.Format("Templates.Status.ImportedWithClashes", result.Imported, result.Duplicates.Count);

        await ReloadAsync();
    }

    public ObservableCollection<TemplateImportViewModel> PendingImport { get; } = [];

    public bool HasPendingImport => PendingImport.Count > 0;

    [RelayCommand]
    private async Task OverrideImports()
    {
        var written = await _userTemplates.OverrideAsync([.. PendingImport.Select(x => x.Duplicate)], CancellationToken.None);

        PendingImport.Clear();
        OnPropertyChanged(nameof(HasPendingImport));

        Status = _localization.Format("Templates.Status.Overridden", written);

        await ReloadAsync();
    }

    [RelayCommand]
    private void CancelImports()
    {
        PendingImport.Clear();
        OnPropertyChanged(nameof(HasPendingImport));

        Status = null;
    }
}

public sealed class TemplateOption(ProjectTemplate template, bool isFavorite, LocalizationService localization)
{
    public string Id { get; } = template.Id;

    public AlcomTemplate? Definition { get; } = template.Definition;

    public string Display { get; } = template.IsAvailable
        ? template.DisplayName
        : localization.Format("Templates.Unavailable", template.DisplayName);

    public IReadOnlyList<UnityVersion> UnityVersions { get; } = template.UnityVersions;

    public string FavoriteAction { get; } =
        localization[isFavorite ? "Templates.Unfavorite" : "Templates.Favorite"];

    public string? SourceFile { get; } = template.SourceFile;

    public bool IsOwn => SourceFile is not null;

    public bool IsAvailable { get; } = template.IsAvailable;

    public override string ToString() => Display;
}

/// <summary>「额外的包」里的一条。</summary>
public sealed partial class TemplateDependencyViewModel : ObservableObject
{
    private readonly PackageCatalog _catalog;

    public TemplateDependencyViewModel(string package, string range, PackageCatalog catalog)
    {
        _catalog = catalog;
        _package = package;
        _range = range;

        RefreshVersions();
    }

    [ObservableProperty]
    private string _package;

    [ObservableProperty]
    private string _range;

    /// <summary>该包可选版本；认不出来时为空。</summary>
    public ObservableCollection<string> Versions { get; } = [];

    partial void OnPackageChanged(string value) => RefreshVersions();

    private void RefreshVersions()
    {
        Versions.Clear();

        foreach (var version in _catalog.GetVersions(Package).OrderByDescending(x => x.Version))
        {
            Versions.Add(version.Version.ToString());
        }
    }
}

public sealed class TemplateImportViewModel(TemplateImportDuplicate duplicate, LocalizationService localization)
{
    public TemplateImportDuplicate Duplicate { get; } = duplicate;

    public string Display { get; } =
        localization.Format("Templates.ImportClash", duplicate.ImportingName, duplicate.ExistingName);
}
