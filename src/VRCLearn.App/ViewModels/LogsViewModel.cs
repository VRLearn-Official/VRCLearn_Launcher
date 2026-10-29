using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.Logging;
using VRCLearn.Infrastructure.Shell;

namespace VRCLearn.App.ViewModels;

public sealed partial class LogsViewModel : PageViewModelBase, IPageViewModel
{
    private readonly AppPaths _paths;
    private readonly FileLoggerProvider _logFile;
    private readonly LocalizationService _localization;
    private readonly ILogger<LogsViewModel> _logger;
    private readonly IReadOnlyList<Option> _levelOptions;

    public LogsViewModel(
        AppPaths paths,
        FileLoggerProvider logFile,
        LocalizationService localization,
        ToastService toasts,
        ILogger<LogsViewModel> logger)
        : base(toasts)
    {
        _paths = paths;
        _logFile = logFile;
        _localization = localization;
        _logger = logger;
        _levelOptions =
        [
            new(nameof(LogLevel.Information), () => localization["Logs.Level.Information"]),
            new(nameof(LogLevel.Warning), () => localization["Logs.Level.Warning"]),
            new(nameof(LogLevel.Error), () => localization["Logs.Level.Error"]),
        ];

        _localization.PropertyChanged += (_, _) => LocalizedOptions.Refresh(_levelOptions);
    }

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public string LogFilePath => _logFile.Path;

    public IReadOnlyList<Option> LevelOptions => _levelOptions;

    [ObservableProperty]
    private string _selectedLevel = nameof(LogLevel.Information);

    partial void OnSelectedLevelChanged(string value) => Reload(force: true);

    [ObservableProperty]
    private bool _autoRefresh = true;

    [ObservableProperty]
    private string _text = "";

    private long _readLength = -1;
    private DateTime _readWrite;

    public bool IsEmpty => Text.Length == 0;

    public bool HasContent => Text.Length > 0;

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasContent));
    }

    public void Release()
    {
        Text = "";

        _readLength = -1;
    }

    public void Reload() => Reload(force: false);

    private void Reload(bool force)
    {
        var grown = HasLogChanged();

        if (!force && !grown)
        {
            return;
        }

        if (grown)
        {
            RememberLogState();
        }

        var minimum = Enum.TryParse<LogLevel>(SelectedLevel, out var level) ? level : LogLevel.Information;

        var lines = LogFile
            .ReadTail(LogFilePath)
            .Where(x => x.Level >= minimum)
            .Select(x => x.Text)
            .ToList();

        var text = string.Join(Environment.NewLine, lines);
        var changed = text != Text;

        Text = text;

        if (changed)
        {
            ScrolledToEnd?.Invoke();
        }
    }

    private bool HasLogChanged()
    {
        var file = new FileInfo(LogFilePath);

        return file.Exists
            ? file.Length != _readLength || file.LastWriteTimeUtc != _readWrite
            : Text.Length > 0;
    }

    private void RememberLogState()
    {
        var file = new FileInfo(LogFilePath);

        _readLength = file.Exists ? file.Length : -1;
        _readWrite = file.Exists ? file.LastWriteTimeUtc : default;
    }

    public event Action? ScrolledToEnd;

    [RelayCommand]
    private void Refresh() => Reload(force: true);

    [RelayCommand]
    private void OpenFolder() => ShellLauncher.OpenFolder(_paths.LogsDirectory);

    /// <summary>导出整份日志文件，不受当前筛选影响。</summary>
    public async Task ExportAsync(string destination)
    {
        try
        {
            await using var source = new FileStream(
                LogFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            await using var target = File.Create(destination);

            await source.CopyToAsync(target).ConfigureAwait(true);

            Status = _localization.Format("Logs.Exported", destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "导出日志失败");

            Status = _localization.Format("Logs.ExportFailed", exception.Message);
        }
    }
}
