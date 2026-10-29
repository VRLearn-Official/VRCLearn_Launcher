using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly PlaceholderViewModel _placeholder = new();
    private readonly SettingsViewModel _settings;
    private readonly RepositoriesViewModel _repositories;
    private readonly ProjectsViewModel _projects;
    private readonly ProjectDetailViewModel _projectDetail;
    private readonly TemplatesViewModel _templates;
    private readonly SetupViewModel _setup;
    private readonly LogsViewModel _logs;
    private readonly BackupsViewModel _backups;
    private readonly ResourcesViewModel _resources;
    private readonly PluginsViewModel _plugins;
    private readonly GuiConfigStore _guiConfig;
    private readonly LocalizationService _localization;
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        SettingsViewModel settings,
        RepositoriesViewModel repositories,
        ProjectsViewModel projects,
        ProjectDetailViewModel projectDetail,
        TemplatesViewModel templates,
        SetupViewModel setup,
        LogsViewModel logs,
        BackupsViewModel backups,
        ResourcesViewModel resources,
        PluginsViewModel plugins,
        GuiConfigStore guiConfig,
        UpdateViewModel update,
        NavigationService navigation,
        LocalizationService localization,
        ExperimentalFeaturesService experimental,
        ProgressService progress,
        ToastService toasts,
        ILogger<MainWindowViewModel> logger)
    {
        Experimental = experimental;
        Progress = progress;
        Toasts = toasts;
        _logger = logger;
        _guiConfig = guiConfig;
        _localization = localization;
        Update = update;
        _settings = settings;
        _repositories = repositories;
        _projects = projects;
        _projectDetail = projectDetail;
        _templates = templates;
        _setup = setup;
        _logs = logs;
        _backups = backups;
        _resources = resources;
        _plugins = plugins;
        _currentPage = _placeholder;

        navigation.Requested += Navigate;
        projectDetail.PropertyChanged += OnProjectDetailChanged;
        localization.PropertyChanged += OnLocalizationChanged;
        update.PropertyChanged += OnUpdateChanged;
    }

    public string SettingsNavLabel =>
        Update.HasNotice ? _localization["Nav.UpdateAvailable"] : _localization["Nav.Settings"];

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateViewModel.HasNotice))
        {
            OnPropertyChanged(nameof(SettingsNavLabel));
        }
    }

    public ExperimentalFeaturesService Experimental { get; }

    private void OnProjectDetailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (CurrentKey == "project-detail")
        {
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(SettingsNavLabel));
    }

    private static readonly Dictionary<string, (string Title, string Subtitle)> Headers = new()
    {
        ["projects"] = ("Nav.Projects", "Projects.Subtitle"),
        ["backups"] = ("Nav.Backups", "Backups.Subtitle"),
        ["resources"] = ("Nav.Resources", "Resources.Subtitle"),
        ["repositories"] = ("Nav.Repositories", "Repositories.Subtitle"),
        ["templates"] = ("Nav.Templates", "Templates.Subtitle"),
        ["logs"] = ("Nav.Logs", "Logs.Subtitle"),
        ["plugins"] = ("Nav.Plugins", "Plugins.Subtitle"),
        ["settings"] = ("Nav.Settings", "Settings.Subtitle"),
        ["setup"] = ("Setup.Heading", ""),
    };

    public string PageTitle => CurrentKey == "project-detail"
        ? _projectDetail.ProjectName
        : _localization[Headers.GetValueOrDefault(CurrentKey).Title ?? ""];

    public string PageSubtitle => CurrentKey == "project-detail"
        ? _projectDetail.HeaderSubtitle
        : _localization[Headers.GetValueOrDefault(CurrentKey).Subtitle ?? ""];

    [ObservableProperty]
    private object _currentPage;

    public string CurrentKey { get; private set; } = "projects";

    public bool IsFirstRunSetup => CurrentKey == "setup" && !_setup.IsRerun;

    public bool IsNotFirstRunSetup => !IsFirstRunSetup;

    public bool ShowDropZone => CurrentKey is "projects" or "backups" or "templates" or "plugins";

    public void Navigate(string key)
    {
        SetCurrentKey(key);
        CurrentPage = PageFor(key);
    }

    private void SetCurrentKey(string key)
    {
        if (CurrentKey == key)
        {
            return;
        }

        CurrentKey = key;
        OnPropertyChanged(nameof(CurrentKey));
        OnPropertyChanged(nameof(IsFirstRunSetup));
        OnPropertyChanged(nameof(IsNotFirstRunSetup));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(ShowDropZone));

        _logger.LogInformation("打开页面：{Page}", key);
    }

    private object PageFor(string key) => key switch
    {
        "settings" => _settings,
        "repositories" => _repositories,
        "projects" => _projects,
        "project-detail" => _projectDetail,
        "templates" => _templates,
        "setup" => _setup,
        "logs" => _logs,
        "backups" => _backups,
        "resources" => _resources,
        "plugins" => _plugins,
        _ => _placeholder,
    };

    public void Start()
    {
        if (_guiConfig.Current.SetupCompleted)
        {
            Navigate("projects");
            return;
        }

        _setup.Restart();
        SetCurrentKey("setup");
        CurrentPage = _setup;
    }

    public UpdateViewModel Update { get; }

    public ProgressService Progress { get; }

    public ToastService Toasts { get; }
}
