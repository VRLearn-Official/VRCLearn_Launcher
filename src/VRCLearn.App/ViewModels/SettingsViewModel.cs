using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure;
using VRCLearn.Infrastructure.Http;
using VRCLearn.Infrastructure.Logging;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly GuiConfigStore _store;
    private readonly ThemeService _theme;
    private readonly LocalizationService _localization;
    private readonly MutableProxy _proxy;
    private readonly ConfigBackupService _configBackup;
    private readonly ILauncherSettingsStore _settings;
    private readonly WindowsProtocolRegistration _protocols;
    private readonly SetupViewModel _setup;
    private readonly NavigationService _navigation;
    private readonly IUnityEditorLocator _editors;
    private readonly UnityEditorRegistration _editorRegistration;
    private readonly AnimationService _animations;
    private readonly LogLevelFilter _logLevels;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IReadOnlyList<Option> _buttonColors;
    private readonly IReadOnlyList<Option> _backupFormats;
    private readonly IReadOnlyList<Option> _backupScopes;
    private readonly IReadOnlyList<Option> _hubAccessMethods;

    public SettingsViewModel(
        AppPaths paths,
        GuiConfigStore store,
        SharedSettingsViewModel shared,
        ThemeService theme,
        LocalizationService localization,
        MutableProxy proxy,
        BackupDirectoryViewModel backups,
        ConfigBackupService configBackup,
        ILauncherSettingsStore settings,
        WindowsProtocolRegistration protocols,
        SetupViewModel setup,
        UpdateViewModel update,
        AnimationService animations,
        LogLevelFilter logLevels,
        NavigationService navigation,
        IUnityEditorLocator editors,
        UnityEditorRegistration editorRegistration,
        DialogService dialogs,
        ToastService toasts,
        PackageCacheViewModel cache,
        ILogger<SettingsViewModel> logger)
    {
        _logger = logger;
        _editors = editors;
        _editorRegistration = editorRegistration;
        _dialogs = dialogs;
        _toasts = toasts;
        Cache = cache;
        _paths = paths;
        _store = store;
        _theme = theme;
        _localization = localization;
        _proxy = proxy;
        Backups = backups;
        _configBackup = configBackup;
        _settings = settings;
        _protocols = protocols;
        _setup = setup;
        Update = update;
        _animations = animations;
        _logLevels = logLevels;
        _navigation = navigation;

        Shared = shared;

        _protocols.Applied += (_, _) => RefreshProtocolState();
        _settings.Changed += OnSettingsChanged;

        _buttonColors = LocalizedOptions.ButtonColors(localization);
        _backupFormats =
        [
            new("default", () => localization["Settings.BackupFormat.Default"]),
            new("zip-store", () => localization["Settings.BackupFormat.Store"]),
            new("zip-best", () => localization["Settings.BackupFormat.Best"]),
        ];
        _backupScopes =
        [
            new("config", () => localization["Settings.ManagerBackup.Config"]),
            new("full", () => localization["Settings.ManagerBackup.Full"]),
        ];
        _hubAccessMethods =
        [
            new("read-config", () => localization["Settings.UnityHubAccess.ReadConfig"]),
            new("call-hub", () => localization["Settings.UnityHubAccess.CallHub"]),
        ];

        _selectedButtonColor = _store.Current.ButtonColor;
        _animationEnabled = _store.Current.AnimationEnabled;
        _proxyEnabled = _store.Current.ProxyEnabled;

        (_proxyScheme, _proxyHost, _proxyPort) = ProxyAddress.Split(_store.Current.ProxyUrl);
        _selectedBackupFormat = _store.Current.BackupFormat;
        _selectedBackupScope = "config";
        _experimentalFeatures = _store.Current.ExperimentalFeatures;
        _selectedLogLevel = _store.Current.LogsLevel;
        _logsRetentionCount = _store.Current.LogsRetentionCount;
        _showPrereleasePackages = _settings.Current.ShowPrereleasePackages;
        _actualProtocolCommand = WindowsProtocolRegistration.Describe(WindowsProtocolRegistration.VccScheme);
        _actualTemplateProgId = WindowsProtocolRegistration.DescribeFileType();
        _defaultUnityArguments = string.Join(Environment.NewLine, _store.Current.DefaultUnityArguments);
        _unityHubAccessMethod = HubAccessMethods.Any(x => x.Value == _store.Current.UnityHubAccessMethod)
            ? _store.Current.UnityHubAccessMethod
            : "read-config";

        ReloadUnityEditors();

        _localization.PropertyChanged += (_, _) =>
        {
            LocalizedOptions.Refresh(_buttonColors);
            LocalizedOptions.Refresh(_backupFormats);
            LocalizedOptions.Refresh(_hubAccessMethods);
        };
    }

    public SharedSettingsViewModel Shared { get; }

    public IReadOnlyList<Option> ButtonColors => _buttonColors;

    public IReadOnlyList<Option> BackupFormats => _backupFormats;

    public IReadOnlyList<Option> BackupScopes =>
        ExperimentalFeatures ? _backupScopes : [_backupScopes[0]];

    [ObservableProperty]
    private bool _experimentalFeatures;

    partial void OnExperimentalFeaturesChanged(bool value)
    {
        _store.Current.ExperimentalFeatures = value;
        _store.Save();

        if (!value && SelectedBackupScope == "full")
        {
            SelectedBackupScope = "config";
        }

        OnPropertyChanged(nameof(BackupScopes));

        _logger.LogInformation("实验性功能：{Value}", value);
    }

    [ObservableProperty]
    private string _selectedBackupScope;

    partial void OnSelectedBackupScopeChanged(string value)
    {
        if (value == "full")
        {
            _dialogs.ShowNotice(
                _localization["Settings.ManagerBackup.FullWarningTitle"],
                _localization["Settings.ManagerBackup.FullWarning"],
                _localization["Dialog.Confirm"]);
        }
    }

    public string DataDirectory => _paths.Root;

    // ---- 包缓存 ----

    public PackageCacheViewModel Cache { get; }

    [ObservableProperty]
    private string _selectedButtonColor;

    [ObservableProperty]
    private bool _animationEnabled;

    [ObservableProperty]
    private bool _proxyEnabled;

    [ObservableProperty]
    private string _proxyScheme = ProxyAddress.DefaultScheme;

    [ObservableProperty]
    private string _proxyHost = "";

    [ObservableProperty]
    private string _proxyPort = "";

    public IReadOnlyList<string> ProxySchemes => ProxyAddress.Schemes;

    [ObservableProperty]
    private string _selectedBackupFormat;

    public BackupDirectoryViewModel Backups { get; }

    public IReadOnlyList<Option> HubAccessMethods => _hubAccessMethods;

    [ObservableProperty]
    private string _unityHubAccessMethod = "read-config";

    partial void OnUnityHubAccessMethodChanged(string value)
    {
        _store.Current.UnityHubAccessMethod = value;
        _store.Save();

        _logger.LogInformation("Unity Hub 访问方式：{Value}", value);
    }

    // ---- 手动指定的 Unity 编辑器 ----

    public ObservableCollection<UnityEditorItem> ManualUnityEditors { get; } = [];

    public bool HasNoManualUnityEditors => ManualUnityEditors.Count == 0;

    public void AddUnityEditor(string executablePath)
    {
        if (_editorRegistration.Add(executablePath))
        {
            ReloadUnityEditors();
        }
    }

    [RelayCommand]
    private void RemoveUnityEditor(UnityEditorItem item)
    {
        _settings.Current.UnityEditors.RemoveAll(x => string.Equals(x, item.Path, StringComparison.OrdinalIgnoreCase));
        _settings.Save();

        _logger.LogInformation("移除 Unity 编辑器：{Path}", item.Path);

        ReloadUnityEditors();
    }

    private void ReloadUnityEditors()
    {
        ManualUnityEditors.Clear();

        foreach (var path in _settings.Current.UnityEditors)
        {
            ManualUnityEditors.Add(new UnityEditorItem(_editors.Probe(path), path));
        }

        OnPropertyChanged(nameof(HasNoManualUnityEditors));
    }

    // ---- Unity 启动参数 ----

    /// <summary>一行一条。</summary>
    [ObservableProperty]
    private string _defaultUnityArguments;

    partial void OnDefaultUnityArgumentsChanged(string value)
    {
        _store.Current.DefaultUnityArguments =
            [.. value.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0)];

        _store.Save();
    }

    partial void OnSelectedButtonColorChanged(string value)
    {
        _store.Current.ButtonColor = value;
        _theme.ApplyButtonColor(value);
        _store.Save();

        _logger.LogInformation("按钮颜色：{Color}", value);
    }

    public IReadOnlyList<Option> LogLevels =>
    [
        new("Debug", "Debug"),
        new("Information", "Information"),
        new("Warning", "Warning"),
        new("Error", "Error"),
    ];

    [ObservableProperty]
    private string _selectedLogLevel = "Information";

    partial void OnSelectedLogLevelChanged(string value)
    {
        _store.Current.LogsLevel = value;
        _store.Save();

        _logLevels.Apply(value);
        _logger.LogInformation("日志级别：{Level}", value);
    }

    [ObservableProperty]
    private int _logsRetentionCount;

    partial void OnLogsRetentionCountChanged(int value)
    {
        _store.Current.LogsRetentionCount = value;
        _store.Save();

        _logger.LogInformation("日志保留份数：{Count}", value);
    }

    [ObservableProperty]
    private bool _showPrereleasePackages;

    partial void OnShowPrereleasePackagesChanged(bool value)
    {
        _settings.Current.ShowPrereleasePackages = value;
        _settings.Save();

        _logger.LogInformation("显示预发布包：{Value}", value);
    }

    partial void OnAnimationEnabledChanged(bool value)
    {
        _store.Current.AnimationEnabled = value;
        _store.Save();

        _animations.Apply(value);
        _logger.LogInformation("界面动画：{Value}", value);
    }

    partial void OnProxyEnabledChanged(bool value)
    {
        _store.Current.ProxyEnabled = value;
        ApplyProxy();

        _logger.LogInformation("下载代理：{Value}", value);
    }

    partial void OnProxySchemeChanged(string value) => ApplyProxy();

    partial void OnProxyHostChanged(string value) => ApplyProxy();

    partial void OnProxyPortChanged(string value) => ApplyProxy();

    partial void OnSelectedBackupFormatChanged(string value)
    {
        _store.Current.BackupFormat = value;
        _store.Save();

        _logger.LogInformation("备份压缩格式：{Format}", value);
    }

    [ObservableProperty]
    private string _actualProtocolCommand = "";

    public bool HasProtocolCommand => ActualProtocolCommand.Length > 0;

    partial void OnActualProtocolCommandChanged(string value) =>
        OnPropertyChanged(nameof(HasProtocolCommand));

    [ObservableProperty]
    private string _actualTemplateProgId = "";

    public bool HasTemplateProgId => ActualTemplateProgId.Length > 0;

    partial void OnActualTemplateProgIdChanged(string value) =>
        OnPropertyChanged(nameof(HasTemplateProgId));

    public UpdateViewModel Update { get; }

    [RelayCommand]
    private void RerunSetup()
    {
        _setup.Restart();
        _navigation.GoTo("setup");
    }

    [RelayCommand]
    private async Task Backup()
    {
        var scope = SelectedBackupScope == "full" ? BackupScope.Full : BackupScope.Config;
        var full = scope == BackupScope.Full;

        _dialogs.ShowNotice(
            _localization["Settings.ConfigBackup"],
            _localization[full ? "Settings.ManagerBackup.FullHint" : "Settings.ConfigBackupHint"],
            _localization["Dialog.Confirm"]);

        var path = _dialogs.PickSaveFile(
            _localization["Settings.ConfigBackup"],
            $"VRCLearn-{(full ? "full" : "config")}-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            "*.zip|*.zip|*|*");

        if (path is null)
        {
            return;
        }

        try
        {
            var result = await Task.Run(() => _configBackup.Backup(path, scope));

            _toasts.Show(result.Files == 0
                ? _localization["Settings.ConfigBackup.Empty"]
                : _localization.Format(
                    "Settings.ConfigBackup.Done",
                    result.Files,
                    SizeText.Format(result.Bytes),
                    path));
        }
        catch (InsufficientDiskSpaceException exception)
        {
            _logger.LogWarning(exception, "备份配置失败");

            _toasts.Show(_localization.FormatDiskSpace(exception));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "备份配置失败");

            _toasts.Show(_localization.Format("Settings.ConfigBackup.Failed", exception.Message));
        }
    }

    [RelayCommand]
    private async Task Restore()
    {
        _dialogs.ShowNotice(
            _localization["Settings.ConfigRestore"],
            _localization["Settings.ConfigRestore.Hint"],
            _localization["Dialog.Confirm"]);

        var path = _dialogs.PickFile(
            _localization["Settings.ConfigRestore.PickTitle"],
            "*.zip|*.zip|*|*");

        if (path is null)
        {
            return;
        }

        if (!_dialogs.ShowConfirm(
                _localization["Settings.ConfigRestore.ConfirmTitle"],
                _localization["Settings.ConfigRestore.ConfirmMessage"],
                _localization["Settings.ConfigRestore.ConfirmButton"],
                danger: true))
        {
            return;
        }

        try
        {
            string? managedRoot = null;

            if (_configBackup.IsFullBackup(path))
            {
                _dialogs.ShowNotice(
                    _localization["Settings.ConfigRestore.FullTitle"],
                    _localization["Settings.ConfigRestore.FullHint"],
                    _localization["Dialog.Confirm"]);

                managedRoot = _dialogs.PickFolder(
                    _localization["Settings.ConfigRestore.PickFolder"],
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

                if (managedRoot is null)
                {
                    return;
                }
            }

            var result = await Task.Run(() => _configBackup.Restore(path, managedRoot));

            if (result.Files == 0 && result.Failed == 0)
            {
                _toasts.Show(_localization["Settings.ConfigRestore.Empty"]);
                return;
            }

            var message = result.Failed == 0
                ? _localization.Format("Settings.ConfigRestore.Done", result.Files)
                : _localization.Format("Settings.ConfigRestore.Partial", result.Files, result.Failed);

            if (managedRoot is not null)
            {
                message += Environment.NewLine + _localization.Format(
                    "Settings.ConfigRestore.ManagedPath",
                    Path.Combine(managedRoot, ConfigBackupService.ManagedFolderName));
            }

            if (_dialogs.ShowConfirm(
                    _localization["Settings.ConfigRestore.DoneTitle"],
                    message,
                    _localization["Settings.ConfigRestore.Restart"]))
            {
                RestartApp();
            }
        }
        catch (InsufficientDiskSpaceException exception)
        {
            _logger.LogWarning(exception, "恢复配置失败");

            _toasts.Show(_localization.FormatDiskSpace(exception));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(exception, "恢复配置失败");

            _toasts.Show(_localization.Format("Settings.ConfigRestore.Failed", exception.Message));
        }
    }

    private void RestartApp()
    {
        if (Environment.ProcessPath is not { } path)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.ArgumentList.Add(SingleInstance.RestartArgument);

            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "重启程序失败");

            _toasts.Show(_localization["Settings.ConfigRestore.RestartFailed"]);
            return;
        }

        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void OpenLicenses() => _dialogs.ShowLicenses(
        _localization["Settings.Licenses"],
        LicenseText.Read(),
        _localization["Dialog.Confirm"]);

    [RelayCommand]
    private static void OpenFeedback() => ShellLauncher.OpenUrl(Links.Feedback);

    [RelayCommand]
    private static void OpenRepository() => ShellLauncher.OpenUrl(Links.Repository);

    private void RefreshProtocolState()
    {
        ActualProtocolCommand = WindowsProtocolRegistration.Describe(WindowsProtocolRegistration.VccScheme);
        ActualTemplateProgId = WindowsProtocolRegistration.DescribeFileType();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnSettingsChanged(sender, e));

            return;
        }

        ReloadUnityEditors();
        Backups.Refresh();
    }

    private bool _proxyFocus;

    /// <summary>下次显示设置页时，把网络那一节滚到看得见的地方。</summary>
    public void FocusProxy() => _proxyFocus = true;

    public bool ConsumeProxyFocus()
    {
        if (!_proxyFocus)
        {
            return false;
        }

        _proxyFocus = false;
        return true;
    }

    private void ApplyProxy()
    {
        _store.Current.ProxyUrl = ProxyAddress.Compose(ProxyScheme, ProxyHost, ProxyPort);

        _proxy.Configure(ProxyEnabled, _store.Current.ProxyUrl);
        _store.Save();
    }
}


/// <summary>下拉框里的一项，<see cref="Display"/> 随语言变化。</summary>
public sealed class Option : ObservableObject
{
    private readonly Func<string> _display;

    public Option(string value, Func<string> display)
    {
        Value = value;
        _display = display;
    }

    public Option(string value, string display)
        : this(value, () => display)
    {
    }

    public string Value { get; }

    public string Display => _display();

    public override string ToString() => Display;

    public void Refresh() => OnPropertyChanged(nameof(Display));
}

public sealed class UnityEditorItem(UnityEditor? editor, string path)
{
    public string Path { get; } = path;

    public string Display { get; } = editor is null ? path : $"{editor.Version}  ·  {path}";

    public bool IsMissing { get; } = editor is null;
}
