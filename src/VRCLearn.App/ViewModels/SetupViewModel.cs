using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCLearn.App.Services;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Http;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class SetupViewModel : ObservableObject
{
    public const int StepCount = 4;

    private readonly GuiConfigStore _guiConfigStore;
    private readonly ILauncherSettingsStore _settings;
    private readonly LocalizationService _localization;
    private readonly IUnityEditorLocator _editors;
    private readonly UnityEditorRegistration _editorRegistration;
    private readonly NavigationService _navigation;
    private readonly MutableProxy _proxy;

    public SetupViewModel(
        GuiConfigStore guiConfigStore,
        ILauncherSettingsStore settings,
        SharedSettingsViewModel shared,
        LocalizationService localization,
        IUnityEditorLocator editors,
        UnityEditorRegistration editorRegistration,
        NavigationService navigation,
        MutableProxy proxy,
        PackageCacheViewModel cache)
    {
        _guiConfigStore = guiConfigStore;
        _settings = settings;
        _localization = localization;
        _editors = editors;
        _editorRegistration = editorRegistration;
        _navigation = navigation;
        _proxy = proxy;
        Shared = shared;
        Cache = cache;
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

        if (value == EditorStep)
        {
            DetectEditors();
        }
    }

    private const int EditorStep = 2;

    public bool CanGoBack => Step > 0;

    public bool IsNotLastStep => Step < StepCount - 1;

    public bool IsLastStep => !IsNotLastStep;

    public string StepCaption =>
        _localization.Format("Setup.StepCaption", Step + 1, StepCount);

    [ObservableProperty]
    private bool _isBusy;

    // ---- 已装的 Unity ----

    public ObservableCollection<SetupEditorViewModel> InstalledEditors { get; } = [];

    public bool HasNoEditors => InstalledEditors.Count == 0;

    [RelayCommand]
    private void DetectEditors()
    {
        InstalledEditors.Clear();

        var manual = _settings.Current.UnityEditors;

        foreach (var editor in _editors
            .FindEditors()
            .Where(x => UnityReleaseCatalog.SupportedVersions.Contains(x.Version)
                || manual.Contains(x.ExecutablePath, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Version))
        {
            InstalledEditors.Add(new SetupEditorViewModel(editor));
        }

        OnPropertyChanged(nameof(HasNoEditors));
    }

    public void AddUnityEditor(string executablePath)
    {
        if (_editorRegistration.Add(executablePath))
        {
            DetectEditors();
        }
    }

    // ---- 包缓存 ----

    public PackageCacheViewModel Cache { get; }

    // ---- 步骤 ----

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

public sealed class SetupEditorViewModel(UnityEditor editor)
{
    public string Version { get; } = editor.Version.ToString();

    public string Path { get; } = editor.ExecutablePath;
}
