using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Plugins;
using VRCLearn.App.Services;
using VRCLearn.App.Views;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.IO;
using VRCLearn.Core.Plugins;
using VRCLearn.Core.Plugins.Hub;
using VRCLearn.Infrastructure.Plugins;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.ViewModels;

public sealed partial class PluginsViewModel : PageViewModelBase
{
    private readonly PluginManager _manager;
    private readonly PluginHubService _hub;
    private readonly HostDownloadService _download;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _localization;
    private readonly AppPaths _paths;
    private readonly GuiConfigStore _configs;
    private readonly ILogger<PluginsViewModel> _logger;
    private readonly IReadOnlyList<Option> _sortOptions;
    private readonly Dictionary<string, PluginHubUpdateCandidate> _updates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private IReadOnlyDictionary<string, string> _providerNames =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public PluginsViewModel(
        PluginManager manager,
        PluginHubService hub,
        HostDownloadService download,
        DialogService dialogs,
        LocalizationService localization,
        AppPaths paths,
        GuiConfigStore configs,
        ToastService toasts,
        ILogger<PluginsViewModel> logger)
        : base(toasts)
    {
        _manager = manager;
        _hub = hub;
        _download = download;
        _dialogs = dialogs;
        _localization = localization;
        _paths = paths;
        _configs = configs;
        _logger = logger;

        _sortOptions =
        [
            new("name", () => localization["Plugins.Sort.Name"]),
            new("installed", () => localization["Plugins.Sort.Installed"]),
            new("enabled", () => localization["Plugins.Sort.Enabled"]),
        ];

        _selectedSort = configs.Current.PluginsSorting switch
        {
            "installed" => "installed",
            "enabled" => "enabled",
            _ => "name",
        };

        FilteredPlugins = CollectionViewSource.GetDefaultView(Plugins);
        FilteredPlugins.Filter = item => item is PluginItemViewModel plugin && Matches(plugin);

        ApplySort();

        _localization.PropertyChanged += (_, _) =>
        {
            LocalizedOptions.Refresh(_sortOptions);
            Rebuild();
        };
    }

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];

    public ICollectionView FilteredPlugins { get; }

    public IReadOnlyList<Option> SortOptions => _sortOptions;

    public bool HasPlugins => Plugins.Count > 0;

    public bool IsEmpty => !HasPlugins;

    public bool IsSearchEmpty => HasPlugins && FilteredPlugins.IsEmpty;

    public bool IsWorking => IsBusy || IsCheckingUpdates;

    public bool CanCheckUpdates => !IsCheckingUpdates && !IsBusy;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    partial void OnIsCheckingUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(CanCheckUpdates));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(IsBusy))
        {
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(CanCheckUpdates));
        }
    }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedSort = "name";

    partial void OnSearchTextChanged(string value)
    {
        FilteredPlugins.Refresh();
        OnPropertyChanged(nameof(IsSearchEmpty));
    }

    partial void OnSelectedSortChanged(string value)
    {
        ApplySort();

        _configs.Current.PluginsSorting = value;
        _configs.Save();
    }

    private void ApplySort()
    {
        using (FilteredPlugins.DeferRefresh())
        {
            FilteredPlugins.SortDescriptions.Clear();

            switch (SelectedSort)
            {
                case "installed":
                    FilteredPlugins.SortDescriptions.Add(new SortDescription(
                        nameof(PluginItemViewModel.InstalledAt),
                        ListSortDirection.Descending));
                    break;

                case "enabled":
                    FilteredPlugins.SortDescriptions.Add(new SortDescription(
                        nameof(PluginItemViewModel.IsEnabled),
                        ListSortDirection.Descending));
                    break;
            }

            FilteredPlugins.SortDescriptions.Add(new SortDescription(
                nameof(PluginItemViewModel.Name),
                ListSortDirection.Ascending));
        }
    }

    private bool Matches(PluginItemViewModel plugin)
    {
        var query = SearchText.Trim();

        return query.Length == 0
            || plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || plugin.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || plugin.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || plugin.SourceText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Rebuild();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取插件列表失败");

            Status = _localization.Format("Plugins.LoadFailed", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await CheckForUpdatesAsync(force: false);
    }

    [RelayCommand]
    private Task CheckUpdates() => CheckForUpdatesAsync(force: true);

    private async Task CheckForUpdatesAsync(bool force)
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        var targets = Plugins
            .Where(x => x.InstalledVersion is { } && x.Hub is { Length: > 0 })
            .Select(x => new PluginHubTarget(x.Id, x.InstalledVersion!, x.Hub!))
            .ToList();

        if (targets.Count == 0)
        {
            _updates.Clear();
            ApplyUpdates();

            if (force)
            {
                Status = _localization["Plugins.UpdateCheckNone"];
            }

            return;
        }

        IsCheckingUpdates = true;

        try
        {
            var result = await _hub.CheckAsync(targets, force, CancellationToken.None);

            foreach (var failure in result.Failures)
            {
                _logger.LogWarning("插件更新检查失败：{Failure}", failure);
            }

            _updates.Clear();

            foreach (var update in result.Updates)
            {
                _updates[update.PluginId] = update;
            }

            ApplyUpdates();

            if (force)
            {
                if (result.Updates.Count > 0)
                {
                    Status = _localization.Format("Plugins.UpdateCheckFound", result.Updates.Count);
                }
                else if (result.Failures.Count > 0)
                {
                    Status = _localization.Format(
                        "Plugins.UpdateCheckFailed",
                        string.Join("; ", result.Failures.Take(2)));
                }
                else
                {
                    Status = _localization["Plugins.UpdateCheckUpToDate"];
                }
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidOperationException
                or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "检查插件更新失败");

            if (force)
            {
                Status = _localization.Format("Plugins.UpdateCheckFailed", exception.Message);
            }
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private void ApplyUpdates()
    {
        foreach (var item in Plugins)
        {
            item.SetUpdate(CandidateFor(item));
        }
    }

    private PluginHubUpdateCandidate? CandidateFor(PluginItemViewModel item)
    {
        if (item.PendingInstall
            || item.InstalledVersion is not { } installed
            || !_updates.TryGetValue(item.Id, out var candidate)
            || candidate.Version <= installed
            || !PluginHubEndpoint.TryNormalize(item.Hub, out var hub)
            || !string.Equals(hub, candidate.Hub, StringComparison.Ordinal))
        {
            return null;
        }

        return candidate;
    }

    private async Task UpdatePluginAsync(PluginItemViewModel item)
    {
        if (item.Update is not { } candidate)
        {
            return;
        }

        var current = item.InstalledVersion?.ToString() ?? "";

        var messageKey = item.IsLoaded
            ? "Plugins.UpdateMessageRunning"
            : "Plugins.UpdateMessage";

        var confirmed = _dialogs.ShowConfirm(
            _localization["Plugins.UpdateTitle"],
            _localization.Format(messageKey, item.Name, current, candidate.Version.ToString()),
            _localization["Plugins.UpdateConfirm"]);

        if (!confirmed)
        {
            return;
        }

        if (candidate.Size > PluginPackageInstaller.MaxPackageBytes)
        {
            Status = _localization.Format("Plugins.UpdateFailed", _localization["Plugins.UpdateTooLarge"]);
            return;
        }

        await _updateGate.WaitAsync();

        try
        {
            item.IsBusy = true;
            IsBusy = true;

            var directory = Path.Combine(_paths.PluginUpdateTempDirectory, Guid.NewGuid().ToString("N"));

            try
            {
                await _dialogs.RunBusyAsync(
                    _localization["Plugins.UpdateProgressTitle"],
                    _localization.Format(
                        "Plugins.UpdateProgress",
                        item.Name,
                        candidate.Version.ToString()),
                    () => RunUpdateAsync(item, candidate, directory));
            }
            catch (Exception exception) when (
                exception is PluginPackageException or PluginManifestException
                    or InvalidDataException or IOException or UnauthorizedAccessException
                    or HttpRequestException or InvalidOperationException)
            {
                _logger.LogWarning(exception, "更新插件失败：{Id}", item.Id);

                Status = _localization.Format("Plugins.UpdateFailed", exception.Message);
            }
            finally
            {
                DirectoryCleanup.TryDelete(directory);
                item.IsBusy = false;
                IsBusy = false;
            }
        }
        finally
        {
            _updateGate.Release();
            Rebuild();
        }
    }

    private async Task RunUpdateAsync(
        PluginItemViewModel item,
        PluginHubUpdateCandidate candidate,
        string directory)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{item.Id}-{candidate.Version}.vclplugin");

        if (candidate.Sha256 is null)
        {
            _logger.LogWarning(
                "插件市场记录缺少 sha256：{Id} {Version}",
                item.Id,
                candidate.Version);
        }

        await _download.DownloadAsync(
            new DownloadRequest(candidate.PackageUri, path, candidate.Sha256),
            progress: null,
            CancellationToken.None);

        var (manifest, actualSha256) = await _manager
            .InspectPackageAsync(path, CancellationToken.None);

        if (!string.Equals(manifest.Id, item.Id, StringComparison.Ordinal)
            || manifest.Version != candidate.Version)
        {
            Status = _localization["Plugins.UpdatePackageMismatch"];
            return;
        }

        var compatibility = PluginCompatibility.Check(
            manifest,
            PluginHostInfo.Version,
            PluginHostInfo.ApiVersion);

        if (compatibility.Count > 0)
        {
            Status = _localization.Format(
                "Plugins.UpdateFailed",
                string.Join("; ", compatibility));
            return;
        }

        var result = await _manager.UpdateAsync(
            item.Id,
            path,
            candidate.Sha256 ?? actualSha256,
            CancellationToken.None);

        Status = result.Outcome == PluginInstallOutcome.PendingRestart
            ? _localization.Format("Plugins.UpdatePending", item.Name)
            : _localization.Format("Plugins.UpdateDone", item.Name, candidate.Version.ToString());

        _updates.Remove(item.Id);
    }

    [RelayCommand]
    private async Task InstallFromFile()
    {
        var paths = _dialogs.PickFiles(
            _localization["Plugins.InstallTitle"],
            _localization["Plugins.FileFilter"]);

        if (paths.Length > 0)
        {
            await InstallAsync(paths);
        }
    }

    public async Task InstallAsync(IReadOnlyList<string> paths)
    {
        var touched = false;

        foreach (var path in paths)
        {
            PluginManifest manifest;
            string sha256;

            try
            {
                (manifest, sha256) = await _manager
                    .InspectPackageAsync(path, CancellationToken.None);
            }
            catch (Exception exception) when (
                exception is PluginPackageException or PluginManifestException
                    or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "读取插件包失败：{Path}", path);

                Status = _localization.Format("Plugins.InstallInvalid", exception.Message);
                continue;
            }

            var name = manifest.Name.Pick(_localization.CurrentLanguage);

            var trusted = _dialogs.ShowPluginTrust(
                _localization["Plugins.Trust.Title"],
                _localization["Plugins.RiskWarning"],
                BuildTrustInfo(manifest),
                _localization["Plugins.Trust.Install"],
                _localization["Dialog.Cancel"]);

            if (!trusted)
            {
                continue;
            }

            IsBusy = true;

            try
            {
                var result = await _manager.InstallAsync(
                    path,
                    sha256,
                    CancellationToken.None);

                Status = result.Outcome == PluginInstallOutcome.PendingRestart
                    ? _localization.Format("Plugins.InstallPending", name)
                    : _localization.Format("Plugins.InstallDone", name);

                touched = true;
            }
            catch (Exception exception) when (
                exception is PluginPackageException or PluginManifestException
                    or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "安装插件失败：{Path}", path);

                Status = _localization.Format("Plugins.InstallFailed", exception.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        if (touched)
        {
            Rebuild();
        }
    }

    [RelayCommand]
    private void OpenPluginDirectory()
    {
        Directory.CreateDirectory(_paths.PluginsDirectory);
        ShellLauncher.OpenFolder(_paths.PluginsDirectory);
    }

    private void Rebuild()
    {
        var snapshots = _manager.GetSnapshots();

        _providerNames = snapshots
            .Where(x => x.Manifest is not null)
            .SelectMany(x => x.Manifest!.Provides.Select(capability => (
                capability.Capability,
                Name: x.Manifest.Name.Pick(_localization.CurrentLanguage))))
            .GroupBy(x => x.Capability, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Name, StringComparer.Ordinal);

        Plugins.Clear();

        foreach (var snapshot in snapshots)
        {
            Plugins.Add(new PluginItemViewModel(
                snapshot,
                _localization,
                SetEnabledAsync,
                OpenPlugin,
                UninstallPlugin,
                OpenPluginData,
                DescribeMissing,
                UpdatePluginAsync));
        }

        ApplyUpdates();
        FilteredPlugins.Refresh();

        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsSearchEmpty));
    }

    private async Task<bool> SetEnabledAsync(PluginItemViewModel item, bool enabled)
    {
        if (enabled)
        {
            var choice = _dialogs.ShowChoice(
                _localization["Plugins.EnableTitle"],
                _localization["Plugins.RiskWarning"],
                _localization["Plugins.EnableConfirm"],
                confirmDanger: true);

            if (choice != ConfirmChoice.Confirm)
            {
                return false;
            }
        }

        PluginActionFailure? failure;

        try
        {
            failure = await _manager.SetEnabledAsync(item.Id, enabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "切换插件开关失败：{Id}", item.Id);

            Status = _localization["Plugins.EnableFailed"];
            return false;
        }

        if (failure is null)
        {
            Rebuild();
            return true;
        }

        Status = failure.Dependents.Count > 0
            ? _localization.Format(
                "Plugins.EnableBlockedDependents",
                string.Join(", ", failure.Dependents.Select(NameOf)))
            : failure.Missing.Count > 0
                ? _localization.Format(
                    "Plugins.EnableBlockedMissing",
                    string.Join(", ", failure.Missing.Select(DescribeMissing)))
                : _localization["Plugins.EnableFailed"];

        return false;
    }

    private string NameOf(string id) =>
        Plugins.FirstOrDefault(x => x.Id == id)?.Name ?? id;

    private string DescribeMissing(string requirement)
    {
        var capability = requirement.Split(' ', 2)[0];

        return _providerNames.TryGetValue(capability, out var name)
            ? _localization.Format("Plugins.MissingProvider", name)
            : requirement;
    }

    private void OpenPlugin(PluginItemViewModel item)
    {
        var error = _manager.Open(item.Id);

        if (error is null)
        {
            Rebuild();
            return;
        }

        Status = _localization.Format("Plugins.OpenFailed", error);
        Rebuild();
    }

    private async void UninstallPlugin(PluginItemViewModel item)
    {
        try
        {
            if (item.IsEnabled)
            {
                _dialogs.ShowNotice(
                    _localization["Plugins.UninstallBlockedTitle"],
                    _localization["Plugins.UninstallEnabled"],
                    _localization["Dialog.Confirm"]);

                return;
            }

            var confirmed = _dialogs.ShowConfirm(
                _localization["Plugins.UninstallTitle"],
                _localization.Format("Plugins.UninstallMessage", item.Name),
                _localization["Plugins.Uninstall"],
                danger: true);

            if (!confirmed)
            {
                return;
            }

            var result = await _manager.UninstallAsync(item.Id, deleteData: true);

            Status = result.Outcome == PluginUninstallOutcome.PendingRestart
                ? _localization.Format("Plugins.UninstallPending", item.Name)
                : _localization.Format("Plugins.UninstallDone", item.Name);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "卸载插件失败：{Id}", item.Id);

            Status = _localization.Format("Plugins.UninstallFailed", exception.Message);
        }
        catch (InvalidOperationException)
        {
            Status = _localization["Plugins.UninstallEnabled"];
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "卸载插件失败：{Id}", item.Id);

            Status = _localization.Format("Plugins.UninstallFailed", exception.Message);
        }
        finally
        {
            Rebuild();
        }
    }

    private void OpenPluginData(PluginItemViewModel item) =>
        ShellLauncher.OpenFolder(_manager.GetDataDirectory(item.Id));

    private PluginTrustInfo BuildTrustInfo(PluginManifest manifest)
    {
        var capabilities = manifest.Requires.Count == 0
            ? _localization["Plugins.Trust.None"]
            : string.Join(
                ", ",
                manifest.Requires.Select(x => $"{x.Capability} >= {x.MinVersion}"));

        var source = PluginSource.OwnerOf(manifest.Author) is { Length: > 0 } owner
            ? owner
            : _localization["Plugins.Source.Unknown"];

        return new PluginTrustInfo(
            manifest.Name.Pick(_localization.CurrentLanguage),
            manifest.Id,
            manifest.Version.ToString(),
            source,
            PluginSource.LinkOf(manifest.Author),
            capabilities);
    }
}
