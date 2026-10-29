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
    private readonly AppPaths _paths;
    private readonly PluginDirectoryScanner _scanner;
    private readonly PluginStateStore _states;
    private readonly PluginPackageInstaller _installer;
    private readonly PluginWindowService _windows;
    private readonly LocalizationService _localization;
    private readonly ILogger<PluginManager> _logger;
    private readonly PluginServiceRegistry _services = new();
    private readonly Dictionary<string, PluginRuntime> _runtimes = new(StringComparer.Ordinal);
    private IReadOnlyList<PluginSnapshot> _snapshots = [];
    private PluginResolution _resolution = new([], new Dictionary<string, string>(), new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, IReadOnlyList<string>>(), []);

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
        _paths = paths;
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
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public void ApplyPendingActions() => _installer.ApplyPendingActions();

    public IReadOnlyList<PluginSnapshot> GetSnapshots()
    {
        var installed = _scanner.Scan();

        foreach (var id in _runtimes.Keys.Where(id => installed.All(x => x.Id != id)).ToList())
        {
            _windows.Close(id);
            _services.RemoveOwner(id);
            _runtimes.Remove(id);
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

        _windows.Close(id);

        if (_runtimes.Remove(id, out var runtime))
        {
            await runtime.DisposeAsync(_logger);
            _services.RemoveOwner(id);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

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

        if (!EnsureLoaded(snapshot, [], out var error))
        {
            return error;
        }

        var runtime = _runtimes[id];

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

    public bool IsLoaded(string id) =>
        _runtimes.TryGetValue(id, out var runtime) && runtime.IsLoaded;

    public Task<(PluginManifest Manifest, string Sha256)> InspectPackageAsync(
        string path,
        CancellationToken cancellationToken) =>
        _installer.InspectAsync(path, cancellationToken);

    public async Task<PluginInstallResult> InstallAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var (manifest, _) = await _installer
            .InspectAsync(path, cancellationToken);

        var result = await _installer
            .InstallAsync(
                path,
                IsLoaded(manifest.Id),
                cancellationToken,
                expectedSha256);

        GetSnapshots();

        return result;
    }

    public string GetDataDirectory(string id)
    {
        var directory = Path.Combine(_paths.PluginDataDirectory, id);

        Directory.CreateDirectory(directory);

        return directory;
    }

    public async Task<PluginUninstallResult> UninstallAsync(string id, bool deleteData)
    {
        if (_states.IsEnabled(id))
        {
            throw new InvalidOperationException("plugin is enabled");
        }

        _windows.Close(id);

        if (_runtimes.Remove(id, out var runtime))
        {
            await runtime.DisposeAsync(_logger);
            _services.RemoveOwner(id);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return _installer.Uninstall(id, deleteData);
    }

    public void Shutdown()
    {
        _localization.PropertyChanged -= OnLocalizationChanged;
        _windows.CloseAll();

        foreach (var runtime in _runtimes.Values)
        {
            runtime.DisposeAsync(_logger).GetAwaiter().GetResult();
            _services.RemoveOwner(runtime.Installed.Id);
        }

        _runtimes.Clear();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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
            .Any(id => _runtimes.TryGetValue(id, out var dependency) && dependency.Faulted);

        var status = pendingInstall || pendingRemoval
            ? PluginStatus.PendingRestart
            : !enabled
                ? PluginStatus.Disabled
                : missing.Count > 0
                    ? PluginStatus.MissingDependency
                    : _resolution.Cycles.Contains(manifest.Id, StringComparer.Ordinal)
                        || dependencyFaulted
                        || (_runtimes.TryGetValue(manifest.Id, out var runtime) && runtime.Faulted)
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
            pendingRemoval,
            _runtimes.TryGetValue(manifest.Id, out var current) ? current.Error : null);
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

    private bool EnsureLoaded(PluginSnapshot snapshot, HashSet<string> stack, out string error)
    {
        error = "";

        var id = snapshot.Installed.Id;

        if (_runtimes.TryGetValue(id, out var existing))
        {
            if (existing.IsLoaded)
            {
                return true;
            }

            if (existing.Faulted)
            {
                error = existing.Error ?? "plugin failed to load";
                return false;
            }
        }

        if (!snapshot.Enabled || snapshot.Manifest is null)
        {
            error = "plugin is disabled";
            return false;
        }

        if (snapshot.Status is PluginStatus.MissingDependency or PluginStatus.PendingRestart or PluginStatus.Faulted)
        {
            error = snapshot.Status == PluginStatus.MissingDependency
                ? $"missing capability: {string.Join(", ", snapshot.Missing)}"
                : "plugin is not ready";
            return false;
        }

        if (!stack.Add(id))
        {
            error = "plugin dependency cycle";
            return false;
        }

        try
        {
            var dependencies = _resolution.Dependencies.GetValueOrDefault(id, []);

            foreach (var dependency in dependencies)
            {
                var dependencySnapshot = _snapshots.FirstOrDefault(x => x.Installed.Id == dependency);

                if (dependencySnapshot is null || !EnsureLoaded(dependencySnapshot, stack, out error))
                {
                    return false;
                }
            }

            return CreateRuntime(snapshot, out error);
        }
        finally
        {
            stack.Remove(id);
        }
    }

    private bool CreateRuntime(PluginSnapshot snapshot, out string error)
    {
        error = "";

        var manifest = snapshot.Manifest!;
        var directory = snapshot.Installed.Directory;
        var entry = Path.Combine(
            directory,
            manifest.Entry.Assembly.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(entry))
        {
            error = $"entry assembly is missing: {manifest.Entry.Assembly}";
            return false;
        }

        var runtime = new PluginRuntime(snapshot.Installed);
        _runtimes[manifest.Id] = runtime;

        try
        {
            var loadContext = new PluginLoadContext(directory, entry);
            runtime.LoadContext = loadContext;

            var assembly = loadContext.LoadFromAssemblyPath(entry);
            var type = assembly.GetType(manifest.Entry.Type, throwOnError: false);

            if (type is null || !typeof(IVclPlugin).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"type does not implement {nameof(IVclPlugin)}: {manifest.Entry.Type}");
            }

            var dataDirectory = GetDataDirectory(manifest.Id);
            var context = new PluginContext(
                manifest,
                directory,
                dataDirectory,
                _localization,
                _services,
                _windows,
                _logger);

            var instance = (IVclPlugin)Activator.CreateInstance(type)!;

            runtime.Context = context;
            runtime.Instance = instance;

            instance.Initialize(context);

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "加载插件失败：{Id}", manifest.Id);

            runtime.Faulted = true;
            runtime.Error = exception.Message;
            _ = runtime.DisposeAsync(_logger);
            _services.RemoveOwner(manifest.Id);
            _runtimes.Remove(manifest.Id);
            error = exception.Message;

            return false;
        }
    }

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
        foreach (var runtime in _runtimes.Values)
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
