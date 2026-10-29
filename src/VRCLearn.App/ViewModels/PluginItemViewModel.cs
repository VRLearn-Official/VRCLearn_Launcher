using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCLearn.App.Plugins;
using VRCLearn.App.Services;
using VRCLearn.Core.Plugins;
using VRCLearn.Infrastructure.Shell;

namespace VRCLearn.App.ViewModels;

public sealed partial class PluginItemViewModel : ObservableObject
{
    private readonly PluginSnapshot _snapshot;
    private readonly LocalizationService _localization;
    private readonly Func<PluginItemViewModel, bool, Task<bool>> _setEnabled;
    private readonly Action<PluginItemViewModel> _open;
    private readonly Action<PluginItemViewModel> _uninstall;
    private readonly Action<PluginItemViewModel> _openData;
    private readonly Func<string, string> _describeMissing;
    private bool _suppressToggle;

    public PluginItemViewModel(
        PluginSnapshot snapshot,
        LocalizationService localization,
        Func<PluginItemViewModel, bool, Task<bool>> setEnabled,
        Action<PluginItemViewModel> open,
        Action<PluginItemViewModel> uninstall,
        Action<PluginItemViewModel> openData,
        Func<string, string> describeMissing)
    {
        _snapshot = snapshot;
        _localization = localization;
        _setEnabled = setEnabled;
        _open = open;
        _uninstall = uninstall;
        _openData = openData;
        _describeMissing = describeMissing;

        var manifest = snapshot.Manifest;
        var language = localization.CurrentLanguage;

        Id = snapshot.Installed.Id;
        Name = manifest?.Name.Pick(language) ?? snapshot.Installed.Id;
        Description = manifest?.Description.Pick(language) ?? "";
        VersionText = manifest is null ? "" : manifest.Version.ToString();
        SourceText = manifest is null ? "" : SourceName(manifest.Author);
        SourceUrl = manifest is null ? "" : PluginSource.LinkOf(manifest.Author);
        StatusText = BuildStatusText();
        IsWarning = snapshot.Missing.Count > 0
            || snapshot.Status is PluginStatus.MissingDependency or PluginStatus.PendingRestart;
        IsError = snapshot.Status == PluginStatus.Faulted;
        ErrorText = snapshot.Error ?? snapshot.Installed.Error ?? "";
        HasError = ErrorText.Length > 0;
        Icon = LoadIcon(snapshot.Installed.Directory, manifest);
        InstalledAt = Directory.GetCreationTimeUtc(snapshot.Installed.Directory);

        _suppressToggle = true;
        IsEnabled = snapshot.Enabled;
        _suppressToggle = false;

        OpenCommand = new RelayCommand(() => _open(this));
        UninstallCommand = new RelayCommand(() => _uninstall(this));
        OpenDataCommand = new RelayCommand(() => _openData(this));
        OpenSourceCommand = new RelayCommand(OpenSource);
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string VersionText { get; }

    public string SourceText { get; }

    public string SourceUrl { get; }

    public bool HasSource => SourceUrl.Length > 0;

    public bool HasMeta => VersionText.Length > 0;

    public string StatusText { get; }

    public bool HasStatus => StatusText.Length > 0;

    public bool IsWarning { get; }

    public bool IsError { get; }

    public bool HasError { get; }

    public string ErrorText { get; }

    public DateTime InstalledAt { get; }

    public BitmapImage? Icon { get; }

    public bool HasIcon => Icon is not null;

    private string SourceName(string? author) =>
        PluginSource.OwnerOf(author) is { Length: > 0 } owner
            ? owner
            : _localization["Plugins.Source.Unknown"];

    private void OpenSource()
    {
        if (SourceUrl.Length > 0)
        {
            ShellLauncher.OpenUrl(SourceUrl);
        }
    }

    public bool CanOpen => _snapshot.CanOpen && !IsBusy;

    public bool CanToggle => _snapshot.Manifest is not null && !IsBusy;

    public bool CanUninstall => !IsBusy;

    public bool CanOpenData => !IsBusy;

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand UninstallCommand { get; }

    public IRelayCommand OpenDataCommand { get; }

    public IRelayCommand OpenSourceCommand { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(CanOpenData));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressToggle || !CanToggle)
        {
            return;
        }

        _ = ToggleAsync(value);
    }

    private async Task ToggleAsync(bool value)
    {
        IsBusy = true;

        try
        {
            if (!await _setEnabled(this, value))
            {
                _suppressToggle = true;
                IsEnabled = !value;
                _suppressToggle = false;
            }
        }
        catch (Exception)
        {
            _suppressToggle = true;
            IsEnabled = !value;
            _suppressToggle = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildStatusText()
    {
        if (_snapshot.Manifest is null)
        {
            return _localization["Plugins.Status.Broken"];
        }

        if (_snapshot.Status == PluginStatus.PendingRestart)
        {
            return _localization["Plugins.Status.PendingRestart"];
        }

        if (_snapshot.Missing.Count > 0)
        {
            return _localization.Format(
                "Plugins.Status.Missing",
                string.Join(", ", _snapshot.Missing.Select(_describeMissing)));
        }

        return _snapshot.Status switch
        {
            PluginStatus.Disabled => "",
            PluginStatus.Ready => "",
            PluginStatus.Loaded => "",
            PluginStatus.MissingDependency => _localization.Format(
                "Plugins.Status.Missing",
                string.Join(", ", _snapshot.Missing.Select(_describeMissing))),
            PluginStatus.PendingRestart => _localization["Plugins.Status.PendingRestart"],
            PluginStatus.Faulted => _localization.Format(
                "Plugins.Status.Faulted",
                _snapshot.Error ?? ""),
            _ => "",
        };
    }

    private static BitmapImage? LoadIcon(string pluginDirectory, PluginManifest? manifest)
    {
        if (manifest?.Icon is not { Length: > 0 } icon)
        {
            return null;
        }

        var path = Path.Combine(
            pluginDirectory,
            icon.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
