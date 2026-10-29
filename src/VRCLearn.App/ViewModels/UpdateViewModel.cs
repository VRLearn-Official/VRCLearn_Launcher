using System.ComponentModel;
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
}

public sealed partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(5);

    private readonly UpdateService _service;
    private readonly LocalizationService _localization;
    private readonly ILogger<UpdateViewModel> _logger;

    private readonly TransientAction _expiry;

    private string? _downloadUrl;
    private bool _running;

    public UpdateViewModel(
        UpdateService service,
        LocalizationService localization,
        ILogger<UpdateViewModel> logger)
    {
        _service = service;
        _localization = localization;
        _logger = logger;

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

    public bool HasNotice => Phase is UpdatePhase.Available;

    public string ButtonText => Phase switch
    {
        UpdatePhase.UpToDate => _localization["Settings.Update.UpToDate"],
        UpdatePhase.Available => _localization.Format("Settings.Update.Available", Version ?? ""),
        UpdatePhase.Failed => _localization["Settings.Update.Failed"],
        _ => _localization["Settings.CheckUpdate"],
    };

    partial void OnPhaseChanged(UpdatePhase value)
    {
        NotifyTexts();
        ActCommand.NotifyCanExecuteChanged();
    }

    partial void OnVersionChanged(string? value) => NotifyTexts();

    private void NotifyTexts()
    {
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(CurrentVersionText));
    }

    public Task InitializeAsync() => CheckAsync(force: false);

    private bool CanAct() => !_running;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task Act() => Phase == UpdatePhase.Available ? OpenDownload() : CheckAsync(force: true);

    private Task OpenDownload()
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl))
        {
            return Task.CompletedTask;
        }

        try
        {
            ShellLauncher.OpenUrl(_downloadUrl);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(exception, "打开下载页失败");
        }

        return Task.CompletedTask;
    }

    private async Task CheckAsync(bool force)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        Expire();

        var status = await _service.CheckAsync(force);

        _running = false;
        ActCommand.NotifyCanExecuteChanged();

        switch (status.Kind)
        {
            case UpdateKind.UpToDate:
                Phase = UpdatePhase.UpToDate;
                ScheduleExpiry();
                return;

            case UpdateKind.Failed:
                Phase = UpdatePhase.Failed;
                ScheduleExpiry();
                return;
        }

        _downloadUrl = status.DownloadUrl;
        Version = status.Version;
        Phase = UpdatePhase.Available;
    }

    private void ScheduleExpiry() => _expiry.Run();

    private void Expire() => _expiry.Cancel();
}
