using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCLearn.App.Services;
using VRCLearn.Infrastructure.Http;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class SetupViewModel : ObservableObject
{
    public const int StepCount = 2;

    private readonly GuiConfigStore _guiConfigStore;
    private readonly LocalizationService _localization;
    private readonly NavigationService _navigation;
    private readonly MutableProxy _proxy;

    public SetupViewModel(
        GuiConfigStore guiConfigStore,
        SharedSettingsViewModel shared,
        LocalizationService localization,
        NavigationService navigation,
        MutableProxy proxy)
    {
        _guiConfigStore = guiConfigStore;
        _localization = localization;
        _navigation = navigation;
        _proxy = proxy;
        Shared = shared;
    }

    public SharedSettingsViewModel Shared { get; }

    public bool IsRerun => _guiConfigStore.Current.SetupCompleted;

    public void Restart()
    {
        Step = 0;

        OnPropertyChanged(nameof(IsRerun));
    }

    [ObservableProperty]
    private int _step;

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsNotLastStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepCaption));
    }

    public bool CanGoBack => Step > 0;

    public bool IsNotLastStep => Step < StepCount - 1;

    public bool IsLastStep => !IsNotLastStep;

    public string StepCaption =>
        _localization.Format("Setup.StepCaption", Step + 1, StepCount);

    [RelayCommand]
    private void Next()
    {
        if (Step < StepCount - 1)
        {
            Step++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    private void Cancel() => _navigation.GoTo("settings");

    [RelayCommand]
    private void Finish()
    {
        var config = _guiConfigStore.Current;
        config.SetupCompleted = true;
        _guiConfigStore.Save();

        _proxy.Configure(config.ProxyEnabled, config.ProxyUrl);

        _navigation.GoTo("projects");
    }
}
