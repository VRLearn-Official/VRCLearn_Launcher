using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;

namespace VRCLearn.App.ViewModels;

public abstract partial class PageViewModelBase(ToastService toasts) : ObservableObject
{
    protected abstract ILogger Logger { get; }

    protected abstract LocalizationService Localization { get; }

    [ObservableProperty]
    private bool _isBusy;

    public string? Status
    {
        set => toasts.Show(value);
    }

    protected async Task WithBusyAsync(Func<Task> work)
    {
        IsBusy = true;

        try
        {
            await work();
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task<T> WithBusyAsync<T>(Func<Task<T>> work)
    {
        IsBusy = true;

        try
        {
            return await work();
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected Task RunBusyAsync(
        string logMessage,
        object? target,
        Func<Task> work,
        string? failureKey = null) =>
        WithBusyAsync(async () =>
        {
            try
            {
                await work();
            }
            catch (InsufficientDiskSpaceException exception)
            {
                Logger.LogWarning(exception, logMessage, target);

                Status = Localization.FormatDiskSpace(exception);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.LogWarning(exception, logMessage, target);

                if (failureKey is not null)
                {
                    Status = Localization.Format(failureKey, exception.Message);
                }
            }
        });
}
