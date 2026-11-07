using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Updates;

namespace VRCLearn.App.ViewModels;

public enum UpdatePhase
{
    Idle,

    UpToDate,

    Available,

    Failed,

    Downloading,

    DownloadFailed,

    Pending,

    InstallFailed,
}

public sealed partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(5);

    private readonly UpdateService _service;
    private readonly LauncherUpdater _updater;
    private readonly LocalizationService _localization;
    private readonly ILogger<UpdateViewModel> _logger;
    private readonly bool _canInstall;
    private readonly TransientAction _expiry;

    private string? _downloadUrl;
    private bool _checking;

    public UpdateViewModel(
        UpdateService service,
        LauncherUpdater updater,
        LocalizationService localization,
        ILogger<UpdateViewModel> logger)
    {
        _service = service;
        _updater = updater;
        _localization = localization;
        _logger = logger;
        _canInstall = updater.CanInstall;

        _expiry = new TransientAction(NoticeDuration, () =>
        {
            if (Phase is UpdatePhase.UpToDate or UpdatePhase.Failed)
            {
                Phase = UpdatePhase.Idle;
            }
        });

        _localization.PropertyChanged += (_, _) => NotifyTexts();
    }

    public string CurrentVersionText => _service.CurrentVersionText;

    [ObservableProperty]
    private UpdatePhase _phase;

    [ObservableProperty]
    private string? _version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _percent;

    public bool HasNotice => Phase is UpdatePhase.Available or UpdatePhase.Pending
        or UpdatePhase.DownloadFailed or UpdatePhase.InstallFailed;

    public bool IsDownloading => Phase == UpdatePhase.Downloading;

    public bool ShowManualDownload => _canInstall
        && Phase is UpdatePhase.Available or UpdatePhase.DownloadFailed or UpdatePhase.InstallFailed;

    public string ButtonText => Phase switch
    {
        UpdatePhase.UpToDate => _localization["Settings.Update.UpToDate"],
        UpdatePhase.Available when _canInstall => _localization["Settings.Update.Stage"],
        UpdatePhase.Available => _localization.Format("Settings.Update.Available", Version ?? ""),
        UpdatePhase.Pending => _localization["Settings.Update.CancelPending"],
        UpdatePhase.Failed => _localization["Settings.Update.Failed"],
        _ => _localization["Settings.CheckUpdate"],
    };

    public string StatusText => Phase switch
    {
        UpdatePhase.Available when _canInstall => _localization.Format("Settings.Update.StageHint", Version ?? ""),
        UpdatePhase.Downloading => _localization.Format("Settings.Update.Downloading", Percent),
        UpdatePhase.Pending => _localization.Format("Settings.Update.Pending", Version ?? ""),
        UpdatePhase.DownloadFailed => _localization["Settings.Update.DownloadFailed"],
        UpdatePhase.InstallFailed => _localization["Settings.Update.InstallFailed"],
        _ => "",
    };

    public bool HasStatus => StatusText.Length != 0;

    partial void OnPhaseChanged(UpdatePhase value)
    {
        NotifyTexts();
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(ShowManualDownload));
    }

    partial void OnVersionChanged(string? value) => NotifyTexts();

    private void NotifyTexts()
    {
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
    }

    public Task InitializeAsync()
    {
        if (_updater.Pending is { } pending)
        {
            Version = pending.Version;
            Phase = UpdatePhase.Pending;
            return Task.CompletedTask;
        }

        if (_updater.InstallFailed)
        {
            Phase = UpdatePhase.InstallFailed;
            return Task.CompletedTask;
        }

        return CheckAsync(force: false);
    }

    private bool CanAct() => !_checking;

    [RelayCommand(CanExecute = nameof(CanAct), IncludeCancelCommand = true)]
    private async Task Act(CancellationToken cancellationToken)
    {
        switch (Phase)
        {
            case UpdatePhase.Available when _canInstall && Version is { } version:
                await DownloadAsync(version, cancellationToken);
                break;

            case UpdatePhase.Available:
                OpenDownload();
                break;

            case UpdatePhase.Pending:
                try
                {
                    _updater.CancelPending();
                    Phase = UpdatePhase.Available;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(exception, "取消待安装的更新失败");
                }

                break;

            default:
                await CheckAsync(force: true);
                break;
        }
    }

    [RelayCommand]
    private void OpenDownload()
    {
        try
        {
            ShellLauncher.OpenUrl(_downloadUrl ?? _service.ManualDownloadUrl);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(exception, "打开下载页失败");
        }
    }

    private async Task DownloadAsync(string version, CancellationToken cancellationToken)
    {
        Percent = 0;
        Phase = UpdatePhase.Downloading;

        try
        {
            await _updater.StageAsync(version, new Progress<int>(percent => Percent = percent), cancellationToken);
            Phase = UpdatePhase.Pending;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Phase = UpdatePhase.Available;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or HttpRequestException or InvalidDataException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "下载启动器更新失败");
            Phase = UpdatePhase.DownloadFailed;
        }
    }

    private async Task CheckAsync(bool force)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        ActCommand.NotifyCanExecuteChanged();
        _expiry.Cancel();

        try
        {
            var status = await _service.CheckAsync(force);

            switch (status.Kind)
            {
                case UpdateKind.UpToDate:
                    Phase = UpdatePhase.UpToDate;
                    _expiry.Run();
                    break;

                case UpdateKind.Failed:
                    Phase = UpdatePhase.Failed;
                    _expiry.Run();
                    break;

                case UpdateKind.UpdateAvailable:
                    _downloadUrl = status.DownloadUrl;
                    Version = status.Version;
                    Phase = UpdatePhase.Available;
                    break;
            }
        }
        finally
        {
            _checking = false;
            ActCommand.NotifyCanExecuteChanged();
        }
    }
}
