using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

/// <summary>设置页与首次运行向导共用的那一份设置。</summary>
public sealed partial class SharedSettingsViewModel : ObservableObject
{
    private readonly GuiConfigStore _configs;
    private readonly ILauncherSettingsStore _settings;
    private readonly IBackupStore _backups;
    private readonly IUnityHubLauncher _hub;
    private readonly LocalizationService _localization;
    private readonly ThemeService _theme;
    private readonly WindowsProtocolRegistration _protocols;
    private readonly ILogger<SharedSettingsViewModel> _logger;
    private readonly IReadOnlyList<Option> _languages;
    private readonly IReadOnlyList<Option> _themes;

    public SharedSettingsViewModel(
        GuiConfigStore configs,
        ILauncherSettingsStore settings,
        IBackupStore backups,
        IUnityHubLauncher hub,
        LocalizationService localization,
        ThemeService theme,
        WindowsProtocolRegistration protocols,
        ILogger<SharedSettingsViewModel> logger)
    {
        _configs = configs;
        _settings = settings;
        _backups = backups;
        _hub = hub;
        _localization = localization;
        _theme = theme;
        _protocols = protocols;
        _logger = logger;

        _languages = LocalizedOptions.Languages(localization);
        _themes = LocalizedOptions.Themes(localization);

        _localization.PropertyChanged += (_, _) =>
        {
            LocalizedOptions.Refresh(_languages);
            LocalizedOptions.Refresh(_themes);
        };

        var config = configs.Current;

        _selectedLanguage = config.Language;
        _selectedTheme = config.Theme;
        _useForVccProtocol = config.UseForVccProtocol;
        _useForAlcomTemplateFiles = config.UseForAlcomTemplateFiles;
        _unityHubPath = EffectiveHubPath();
        _projectDirectory = settings.Current.DefaultProjectPath;
        _backupDirectory = backups.Directory;

        _configs.Changed += OnStoresChanged;
        _settings.Changed += OnStoresChanged;
    }

    public IReadOnlyList<Option> Languages => _languages;

    [ObservableProperty]
    private string _selectedLanguage;

    partial void OnSelectedLanguageChanged(string value)
    {
        _configs.Current.Language = value;
        _configs.Save();

        _localization.Apply(value);
        _logger.LogInformation("界面语言：{Language}", value);
    }

    public IReadOnlyList<Option> Themes => _themes;

    [ObservableProperty]
    private string _selectedTheme;

    partial void OnSelectedThemeChanged(string value)
    {
        _configs.Current.Theme = value;
        _theme.Apply(value);
        _configs.Save();

        _logger.LogInformation("界面主题：{Theme}", value);
    }

    [ObservableProperty]
    private bool _useForVccProtocol;

    partial void OnUseForVccProtocolChanged(bool value)
    {
        _configs.Current.UseForVccProtocol = value;
        _configs.Save();

        _protocols.Apply(value, _configs.Current.UseForAlcomTemplateFiles);
        _logger.LogInformation("vcc:// 关联：{Value}", value);
    }

    [ObservableProperty]
    private bool _useForAlcomTemplateFiles;

    partial void OnUseForAlcomTemplateFilesChanged(bool value)
    {
        _configs.Current.UseForAlcomTemplateFiles = value;
        _configs.Save();

        _protocols.Apply(_configs.Current.UseForVccProtocol, value);
        _logger.LogInformation("模板文件关联：{Value}", value);
    }

    [ObservableProperty]
    private string _unityHubPath;

    partial void OnUnityHubPathChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Current.UnityHubPath = value;
        _settings.Save();

        OnPropertyChanged(nameof(ActualUnityHubPath));
    }

    public string ActualUnityHubPath =>
        _hub.ExecutablePath is { } path ? path : _localization["Settings.UnityHubNotFound"];

    [ObservableProperty]
    private string _projectDirectory;

    partial void OnProjectDirectoryChanged(string value)
    {
        _settings.Current.DefaultProjectPath = value;
        _settings.Save();

        OnPropertyChanged(nameof(ActualProjectDirectory));
    }

    public string ActualProjectDirectory => _settings.Current.EffectiveProjectPath;

    [ObservableProperty]
    private string _backupDirectory;

    partial void OnBackupDirectoryChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Current.ProjectBackupPath = value;
        _settings.Save();
    }

    private string EffectiveHubPath() =>
        string.IsNullOrWhiteSpace(_settings.Current.UnityHubPath)
            ? _hub.ExecutablePath ?? ""
            : _settings.Current.UnityHubPath;

    private bool _syncing;

    private void OnStoresChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(SyncFromStores);

            return;
        }

        SyncFromStores();
    }

    private void SyncFromStores()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;

        try
        {
            var config = _configs.Current;

            SelectedLanguage = config.Language;
            SelectedTheme = config.Theme;
            UseForVccProtocol = config.UseForVccProtocol;
            UseForAlcomTemplateFiles = config.UseForAlcomTemplateFiles;

            var settings = _settings.Current;

            UnityHubPath = EffectiveHubPath();
            ProjectDirectory = settings.DefaultProjectPath;
            BackupDirectory = _backups.Directory;
        }
        finally
        {
            _syncing = false;
        }

        OnPropertyChanged(nameof(ActualUnityHubPath));
        OnPropertyChanged(nameof(ActualProjectDirectory));
    }
}
