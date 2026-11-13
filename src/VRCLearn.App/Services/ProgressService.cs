using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCLearn.Core.Projects;

namespace VRCLearn.App.Services;

/// <summary>耗时操作期间铺满整窗的进度画面。</summary>
public sealed partial class ProgressService(LocalizationService localization) : ObservableObject
{
    private CancellationTokenSource? _cancellation;
    private bool _cancelled;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCancellable;

    [ObservableProperty]
    private bool _showHint = true;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string _detail = "";

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private string _countText = "";

    public bool HasDetail => Detail.Length > 0;

    public bool WasCancelled => _cancelled;

    public Task RunAsync(string title, Func<IProgress<FileProgress>, Task> work) =>
        RunCoreAsync(
            title,
            async (reporter, _) =>
            {
                await work(reporter);

                return true;
            },
            cancellable: false);

    public Task<T> RunAsync<T>(string title, Func<IProgress<FileProgress>, Task<T>> work) =>
        RunCoreAsync(title, (reporter, _) => work(reporter), cancellable: false);

    public Task<T> RunAsync<T>(
        string title,
        Func<IProgress<FileProgress>, CancellationToken, Task<T>> work,
        bool showHint = true) =>
        RunCoreAsync(title, work, cancellable: true, showHint);

    [RelayCommand]
    private void Cancel()
    {
        if (_cancellation is not { } cancellation)
        {
            return;
        }

        _cancelled = true;
        IsCancellable = false;
        Detail = localization["Progress.Cancelling"];
        cancellation.Cancel();
    }

    private async Task<T> RunCoreAsync<T>(
        string title,
        Func<IProgress<FileProgress>, CancellationToken, Task<T>> work,
        bool cancellable,
        bool showHint = true)
    {
        Title = title;
        IsIndeterminate = true;
        Percent = 0;
        CountText = localization["Progress.Preparing"];
        Detail = "";
        ShowHint = showHint;
        IsCancellable = cancellable;
        IsRunning = true;
        _cancelled = false;

        using var cancellation = cancellable ? new CancellationTokenSource() : null;
        _cancellation = cancellation;

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);

            return await work(
                new Progress<FileProgress>(Report),
                cancellation?.Token ?? CancellationToken.None);
        }
        finally
        {
            _cancellation = null;
            IsCancellable = false;
            IsRunning = false;
            Detail = "";
        }
    }

    private void Report(FileProgress progress)
    {
        if (_cancelled)
        {
            return;
        }

        IsIndeterminate = false;
        Percent = progress.Total == 0 ? 0 : progress.Done * 100.0 / progress.Total;
        CountText = localization.Format("Progress.Count", progress.Done, progress.Total);
        Detail = progress.Label is { Length: > 0 } label
            ? localization.Format("Progress.Processing", label)
            : "";
    }
}
