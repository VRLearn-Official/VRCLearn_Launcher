using System.IO;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Plugins;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

internal sealed class PluginLoader(
    AppPaths paths,
    PluginServiceRegistry services,
    PluginWindowService windows,
    LocalizationService localization,
    ILogger<PluginManager> logger)
{
    private readonly Dictionary<string, PluginRuntime> _runtimes = new(StringComparer.Ordinal);

    public IEnumerable<string> Ids => _runtimes.Keys;

    public IEnumerable<PluginRuntime> Runtimes => _runtimes.Values;

    public PluginRuntime? Get(string id) => _runtimes.GetValueOrDefault(id);

    public bool IsLoaded(string id) => _runtimes.TryGetValue(id, out var runtime) && runtime.IsLoaded;

    public string GetDataDirectory(string id)
    {
        var directory = Path.Combine(paths.PluginDataDirectory, id);

        Directory.CreateDirectory(directory);

        return directory;
    }

    public bool EnsureLoaded(
        PluginSnapshot snapshot,
        IReadOnlyList<PluginSnapshot> snapshots,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        HashSet<string> stack,
        out string error)
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
            foreach (var dependency in dependencies.GetValueOrDefault(id, []))
            {
                var dependencySnapshot = snapshots.FirstOrDefault(x => x.Installed.Id == dependency);

                if (dependencySnapshot is null || !EnsureLoaded(dependencySnapshot, snapshots, dependencies, stack, out error))
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

            var context = new PluginContext(
                manifest,
                directory,
                GetDataDirectory(manifest.Id),
                localization,
                services,
                windows,
                logger);

            var instance = (IVclPlugin)Activator.CreateInstance(type)!;

            runtime.Context = context;
            runtime.Instance = instance;

            instance.Initialize(context);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "加载插件失败：{Id}", manifest.Id);

            runtime.Faulted = true;
            runtime.Error = exception.Message;
            _ = runtime.DisposeAsync(logger);
            services.RemoveOwner(manifest.Id);
            _runtimes.Remove(manifest.Id);
            error = exception.Message;

            return false;
        }
    }

    public async Task UnloadAsync(string id)
    {
        windows.Close(id);

        if (!_runtimes.Remove(id, out var runtime))
        {
            return;
        }

        await runtime.DisposeAsync(logger);
        services.RemoveOwner(id);

        await Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        });
    }

    public async Task DisposeAllAsync()
    {
        var runtimes = _runtimes.ToArray();

        await Task.WhenAll(runtimes.Select(x => x.Value.DisposeAsync(logger))).ConfigureAwait(false);

        foreach (var (id, _) in runtimes)
        {
            services.RemoveOwner(id);
        }

        _runtimes.Clear();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
