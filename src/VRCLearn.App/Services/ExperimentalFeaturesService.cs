using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.Services;

public sealed class ExperimentalFeaturesService : ObservableObject
{
    private readonly GuiConfigStore _configs;

    public ExperimentalFeaturesService(GuiConfigStore configs)
    {
        _configs = configs;
        configs.Changed += (_, _) => Notify();
    }

    public bool IsEnabled => _configs.Current.ExperimentalFeatures;

    private void Notify()
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            OnPropertyChanged(nameof(IsEnabled));
            return;
        }

        dispatcher.InvokeAsync(() => OnPropertyChanged(nameof(IsEnabled)));
    }
}
