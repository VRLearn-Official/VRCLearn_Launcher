using System.IO;
using System.Net;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Plugins;
using VRCLearn.Infrastructure.Http;
using VRCLearn.Infrastructure.Plugins;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

public sealed class PluginManager : IDisposable
{
    private readonly PluginDirectoryScanner _scanner;
    private readonly PluginStateStore _states;
    private readonly PluginPackageInstaller _installer;
    private readonly PluginWindowService _windows;
    private readonly LocalizationService _localization;
    private readonly ILogger<PluginManager> _logger;
    private readonly PluginServiceRegistry _services = new();
    private readonly PluginLoader _loader;
    private IReadOnlyList<PluginSnapshot> _snapshots = [];
    private PluginResolution _resolution = new(
        new Dictionary<string, IReadOnlyList<string>>(),
        new Dictionary<string, IReadOnlyList<string>>(),
        []);

    public PluginManager(
        AppPaths paths,
        PluginDirectoryScanner scanner,
        PluginStateStore states,
        PluginPackageInstaller installer,
        PluginWindowService windows,
        SystemCheckHostService systemCheck,
        SettingsHostService settingsHost,
        HostDownloadService download,
        PluginDialogHostService dialogHost,
        ProjectPickerHostService projectPickerHost,
        RepositoryHostService repositories,
        MutableProxy proxy,
        LocalizationService localization,
        ILogger<PluginManager> logger)
    {
        _scanner = scanner;
        _states = states;
        _installer = installer;
        _windows = windows;
        _localization = localization;
        _logger = logger;

        _services.Register<ISystemCheckHost>("", systemCheck);
        _services.Register<ISettingsHost>("", settingsHost);
        _services.Register<IDownloadService>("", download);
        _services.Register<IPluginDialogHost>("", dialogHost);
        _services.Register<IProjectPickerHost>("", projectPickerHost);
        _services.Register<IRepositoryHost>("", repositories);
        _services.Register<IWebProxy>("", proxy);
        _loader = new PluginLoader(paths, _services, windows, localization, logger);
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public void ApplyPendingActions() => _installer.ApplyPendingActions();

    public IReadOnlyList<PluginSnapshot> GetSnapshots()
    {
        var installed = _scanner.Scan();

        foreach (var id in _loader.Ids.Where(id => installed.All(x => x.Id != id)).ToList())
        {
            _ = _loader.UnloadAsync(id);
        }

        var nodes = installed
            .Where(x => x.Manifest is not null)
            .Select(x => new PluginResolutionNode(
                x.Manifest!.Id,
                x.Manifest.Version,
                _states.IsEnabled(x.Manifest.Id),
                x.Manifest.Provides,
                x.Manifest.Requires));

        _resolution = PluginCapabilityResolver.Resolve(nodes);

        _snapshots =
        [
            .. installed.Select(x => Snapshot(x, installed)).OrderBy(x => x.Installed.Id, StringComparer.Ordinal),
        ];

        return _snapshots;
    }

    public async Task<PluginActionFailure?> SetEnabledAsync(string id, bool enabled)
    {
        var snapshots = GetSnapshots();
        var snapshot = snapshots.FirstOrDefault(x => x.Installed.Id == id);

        if (snapshot?.Manifest is null)
        {
            return new PluginActionFailure(["manifest"], []);
        }

        if (enabled)
        {
            var enabledIds = snapshots
                .Where(x => x.Enabled && x.Manifest is not null)
                .Select(x => x.Installed.Id)
                .Append(id)
                .ToHashSet(StringComparer.Ordinal);

            var candidate = Resolve(snapshots, enabledIds);

            if (candidate.Missing.TryGetValue(id, out var missing))
            {
                return new PluginActionFailure(missing, []);
            }

            if (candidate.Cycles.Contains(id, StringComparer.Ordinal))
            {
                return new PluginActionFailure(["cycle"], []);
            }

            _states.SetEnabled(id, true);

            return null;
        }

        var dependents = _resolution.Dependencies
            .Where(x => x.Value.Contains(id, StringComparer.Ordinal))
            .Select(x => x.Key)
            .Where(x => !string.Equals(x, id, StringComparison.Ordinal) && _states.IsEnabled(x))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        if (dependents.Count > 0)
        {
            return new PluginActionFailure([], dependents);
        }

        await _loader.UnloadAsync(id);

        _states.SetEnabled(id, false);

        return null;
    }

    public string? Open(string id)
    {
        var snapshots = GetSnapshots();
        var snapshot = snapshots.FirstOrDefault(x => x.Installed.Id == id);

        if (snapshot?.Manifest is null)
        {
            return "plugin manifest is missing";
        }

        if (_windows.Activate(id))
        {
            return null;
        }

        if (!snapshot.CanOpen)
        {
            return "plugin is not ready";
        }

        if (!_loader.EnsureLoaded(snapshot, snapshots, _resolution.Dependencies, [], out var error))
        {
            return error;
        }

        var runtime = _loader.Get(id)!;

        try
        {
            var view = runtime.Instance!.CreateView();

            _windows.Show(
                id,
                snapshot.Manifest.Name.Pick(_localization.CurrentLanguage),
                view,
                snapshot.Manifest.Window);

            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "创建插件窗口失败：{Id}", id);

            runtime.Faulted = true;
            runtime.Error = exception.Message;
            _windows.Close(id);

            return exception.Message;
        }
    }

    public bool IsLoaded(string id) => _loader.IsLoaded(id);

    public Task<(PluginManifest Manifest, string Sha256)> InspectPackageAsync(
        string path,
        CancellationToken cancellationToken) =>
        _installer.InspectAsync(path, cancellationToken);

    public async Task<PluginInstallResult> InstallAsync(
        string path,
        string id,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var result = await _installer
            .InstallAsync(
                path,
                IsLoaded(id),
                cancellationToken,
                expectedSha256);

        GetSnapshots();

        return result;
    }

    public async Task<PluginInstallResult> UpdateAsync(
        string id,
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (IsLoaded(id))
        {
            _windows.Close(id);
        }

        return await InstallAsync(path, id, expectedSha256, cancellationToken);
    }

    public string GetDataDirectory(string id) => _loader.GetDataDirectory(id);

    public async Task<PluginUninstallResult> UninstallAsync(string id)
    {
        if (_states.IsEnabled(id))
        {
            throw new InvalidOperationException("plugin is enabled");
        }

        await _loader.UnloadAsync(id);

        return _installer.Uninstall(id);
    }

    public void Shutdown()
    {
        _localization.PropertyChanged -= OnLocalizationChanged;
        _windows.CloseAll();

        var disposal = Task.Run(_loader.DisposeAllAsync);

        if (!disposal.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("插件释放超时，退出时不再等待");
        }
    }

    public void Dispose() => Shutdown();

    private PluginSnapshot Snapshot(InstalledPlugin installed, IReadOnlyList<InstalledPlugin> allInstalled)
    {
        if (installed.Manifest is not { } manifest)
        {
            return new PluginSnapshot(
                installed,
                _states.IsEnabled(installed.Id),
                IsLoaded(installed.Id),
                PluginStatus.Faulted,
                [],
                false,
                installed.Error);
        }

        var enabled = _states.IsEnabled(manifest.Id);
        var loaded = IsLoaded(manifest.Id);
        var pendingInstall = _states.Current.PendingInstalls.ContainsKey(manifest.Id);
        var pendingRemoval = _states.Current.PendingRemovals.Contains(manifest.Id);
        var missing = enabled
            ? _resolution.Missing.GetValueOrDefault(manifest.Id, [])
            : MissingRequirements(manifest, allInstalled);
        var dependencyFaulted = _resolution.Dependencies
            .GetValueOrDefault(manifest.Id, [])
            .Any(id => _loader.Get(id) is { Faulted: true });

        var status = pendingInstall || pendingRemoval
            ? PluginStatus.PendingRestart
            : !enabled
                ? PluginStatus.Disabled
                : missing.Count > 0
                    ? PluginStatus.MissingDependency
                    : _resolution.Cycles.Contains(manifest.Id, StringComparer.Ordinal)
                        || dependencyFaulted
                        || _loader.Get(manifest.Id) is { Faulted: true }
                        ? PluginStatus.Faulted
                        : loaded
                            ? PluginStatus.Loaded
                            : PluginStatus.Ready;

        return new PluginSnapshot(
            installed,
            enabled,
            loaded,
            status,
            missing,
            pendingInstall,
            _loader.Get(manifest.Id)?.Error);
    }

    private IReadOnlyList<string> MissingRequirements(
        PluginManifest manifest,
        IReadOnlyList<InstalledPlugin> installed) =>
        [
            .. manifest.Requires
                .Where(requirement => !installed.Any(candidate =>
                    candidate.Manifest is { } provided
                    && !string.Equals(provided.Id, manifest.Id, StringComparison.Ordinal)
                    && _states.IsEnabled(provided.Id)
                    && provided.Provides.Any(capability =>
                        capability.Capability == requirement.Capability
                        && capability.Version >= requirement.MinVersion)))
                .Select(requirement => $"{requirement.Capability} >= {requirement.MinVersion}"),
        ];

    private PluginResolution Resolve(IReadOnlyList<PluginSnapshot> snapshots, IReadOnlySet<string> enabledIds) =>
        PluginCapabilityResolver.Resolve(
            snapshots
                .Where(x => x.Manifest is not null)
                .Select(x => new PluginResolutionNode(
                    x.Manifest!.Id,
                    x.Manifest.Version,
                    enabledIds.Contains(x.Installed.Id),
                    x.Manifest.Provides,
                    x.Manifest.Requires)));

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var runtime in _loader.Runtimes)
        {
            runtime.Context?.NotifyLanguageChanged();

            if (runtime.Installed.Manifest is { } manifest)
            {
                _windows.UpdateTitle(
                    runtime.Installed.Id,
                    manifest.Name.Pick(_localization.CurrentLanguage));
            }
        }
    }
}
