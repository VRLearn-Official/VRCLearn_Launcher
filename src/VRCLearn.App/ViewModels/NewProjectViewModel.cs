using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.IO;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Templates;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;
using VRCLearn.Core.Vpm;

namespace VRCLearn.App.ViewModels;

public sealed partial class NewProjectViewModel(
    TemplatesViewModel templates,
    IUnityEditorLocator editors,
    IFileIoFactory fileIo,
    ProjectsViewModel projects,
    ProjectDetailViewModel detail,
    NavigationService navigation,
    LocalizationService localization,
    ILauncherSettingsStore settings,
    ILogger<NewProjectViewModel> logger) : ObservableObject
{
    private StatusNotifier? _notifier;

    private StatusNotifier Notifier => _notifier ??= new StatusNotifier(value => Status = value, logger);

    public TemplateOption? Preset { get; set; }

    public ObservableCollection<TemplateOption> Templates { get; } = [];

    public ObservableCollection<Option> UnityVersions { get; } = [];

    public bool HasNoUnityVersion => UnityVersions.Count == 0;

    [ObservableProperty]
    private TemplateOption? _selectedTemplate;

    partial void OnSelectedTemplateChanged(TemplateOption? value) => RebuildUnityVersions();

    [ObservableProperty]
    private Option? _selectedUnityVersion;

    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _parentDirectory = "";

    partial void OnProjectNameChanged(string value) => OnPropertyChanged(nameof(TargetPath));

    partial void OnParentDirectoryChanged(string value) => OnPropertyChanged(nameof(TargetPath));

    public string TargetPath =>
        ProjectName.Trim().Length == 0 || ParentDirectory.Length == 0
            ? ""
            : Path.Combine(ParentDirectory, ProjectName.Trim());

    [ObservableProperty]
    private string? _status;

    partial void OnStatusChanged(string? value) => Notifier.Publish(value);

    [ObservableProperty]
    private bool _isBusy;

    public async Task InitializeAsync()
    {
        await templates.ReloadAsync();

        Templates.Clear();

        foreach (var template in templates.Templates.Where(x => x.IsAvailable))
        {
            Templates.Add(template);
        }

        ParentDirectory = settings.Current.EffectiveProjectPath;

        SelectedTemplate = Preset is { } preset
            ? Templates.FirstOrDefault(x => x.Id == preset.Id)
            : null;

        SelectedTemplate ??= Templates.FirstOrDefault();
    }

    /// <summary>是否真的建出来了。</summary>
    public async Task<bool> CreateAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        if (SelectedTemplate?.Id is not { } id
            || SelectedUnityVersion is not { } versionOption
            || UnityVersion.TryParse(versionOption.Value) is not { } unityVersion)
        {
            return false;
        }

        var name = ProjectName.Trim();

        if (name.Length == 0)
        {
            Status = localization["Templates.Status.NameEmpty"];
            return false;
        }

        if (!ProjectService.IsValidName(name))
        {
            Status = localization.Format("Templates.Status.NameInvalid", name);
            return false;
        }

        if (ParentDirectory.Length == 0)
        {
            Status = localization["Templates.Status.LocationEmpty"];
            return false;
        }

        if (templates.PlanFor(id, unityVersion) is not { } plan)
        {
            Status = localization["Templates.Status.NotUsable"];
            return false;
        }

        var target = Path.Combine(ParentDirectory, name);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            Status = localization.Format("Templates.Status.Occupied", target);
            return false;
        }

        IsBusy = true;
        Status = localization["Templates.Status.Creating"];

        try
        {
            Directory.CreateDirectory(target);

            await ProjectCreator.CreateAsync(
                fileIo.For(target),
                fileIo,
                plan,
                unityVersion,
                name,
                CancellationToken.None);

            await projects.AddAsync(target);
            detail.Open(target, resolveAfterLoad: true);
            navigation.GoTo("project-detail");

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "创建项目失败：{Target}", target);

            Status = localization.Format("Templates.Status.Failed", exception.Message, target);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildUnityVersions()
    {
        var installed = editors.FindEditors().Select(x => x.Version).ToList();
        var previous = SelectedUnityVersion?.Value;

        bool IsInstalled(UnityVersion version) => installed.Contains(version);

        UnityVersions.Clear();

        foreach (var version in (SelectedTemplate?.UnityVersions ?? []).OrderDescending())
        {
            var text = version.ToString();

            UnityVersions.Add(new Option(
                text,
                IsInstalled(version) ? localization.Format("Templates.UnityVersionInstalled", text) : text));
        }

        SelectedUnityVersion =
            UnityVersions.FirstOrDefault(x => x.Value == previous)
            ?? UnityVersions.FirstOrDefault(x => UnityVersion.TryParse(x.Value) is { } v && IsInstalled(v))
            ?? UnityVersions.FirstOrDefault();

        OnPropertyChanged(nameof(HasNoUnityVersion));
    }
}
