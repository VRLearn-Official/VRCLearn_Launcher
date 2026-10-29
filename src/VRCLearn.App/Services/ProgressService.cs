using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCLearn.Core.Projects;

namespace VRCLearn.App.Services;

/// <summary>耗时操作期间铺满整窗的进度画面。</summary>
public sealed partial class ProgressService(LocalizationService localization) : ObservableObject
{
    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private string _countText = "";

    public Task RunAsync(string title, Func<IProgress<FileProgress>, Task> work) =>
        RunAsync(title, async reporter =>
        {
            await work(reporter);

            return true;
        });

    public async Task<T> RunAsync<T>(string title, Func<IProgress<FileProgress>, Task<T>> work)
    {
        Title = title;
        IsIndeterminate = true;
        Percent = 0;
        CountText = localization["Progress.Preparing"];
        IsRunning = true;

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);

            return await work(new Progress<FileProgress>(Report));
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Report(FileProgress progress)
    {
        IsIndeterminate = false;
        Percent = progress.Total == 0 ? 0 : progress.Done * 100.0 / progress.Total;
        CountText = localization.Format("Progress.Count", progress.Done, progress.Total);
    }
}
