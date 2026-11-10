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

    partial void OnSelectedLevelChanged(string value) => _ = ReloadAsync(force: true);

    [ObservableProperty]
    private bool _autoRefresh = true;

    [ObservableProperty]
    private string _text = "";

    private (long Length, DateTime LastWrite) _readStamp = (-1, default);
    private Task? _activeRead;

    public bool IsEmpty => Text.Length == 0;

    public bool HasContent => Text.Length > 0;

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasContent));
    }

    public void Release()
    {
        _activeRead = null;
        _readStamp = (-1, default);
        Text = "";
    }

    public async Task ReloadAsync(bool force = false)
    {
        if (!force && _activeRead is not null)
        {
            return;
        }

        var minimum = Enum.TryParse<LogLevel>(SelectedLevel, out var level) ? level : LogLevel.Information;
        var path = LogFilePath;
        var previousStamp = _readStamp;
        var read = Task.Run(() =>
        {
            var file = new FileInfo(path);
            var stamp = file.Exists ? (file.Length, file.LastWriteTimeUtc) : (-1L, default);
            if (!force && stamp == previousStamp)
            {
                return (stamp, Text: (string?)null);
            }

            var lines = LogFile.ReadTail(path).Where(x => x.Level >= minimum).Select(x => x.Text);
            return (stamp, Text: string.Join(Environment.NewLine, lines));
        });
        _activeRead = read;

        try
        {
            var (stamp, text) = await read;
            // A newer filter, manual refresh or page release supersedes this read.
            if (_activeRead != read || text is null)
            {
                return;
            }

            _readStamp = stamp;
            if (text != Text)
            {
                Text = text;
                ScrolledToEnd?.Invoke();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (force && _activeRead == read)
            {
                Report(exception);
            }
        }
        finally
        {
            if (_activeRead == read)
            {
                _activeRead = null;
            }
        }
    }

    public event Action? ScrolledToEnd;

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync(force: true);

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
