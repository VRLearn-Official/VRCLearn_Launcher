using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.Infrastructure.Plugins;

public sealed class PluginHubStateStore(AppPaths paths, ILogger<PluginHubStateStore> logger)
{
    private const int MaxHubs = 32;

    public PluginHubState Load() =>
        JsonFileStore.Peek<PluginHubState>(paths.PluginHubStateFile) ?? new PluginHubState();

    public void Save(PluginHubState state)
    {
        state.Hubs = [.. state.Hubs.OrderByDescending(x => x.CheckedAt).Take(MaxHubs)];

        JsonFileStore.Save(
            paths.PluginHubStateFile,
            state,
            message => logger.LogWarning("{Message}", message));
    }
}
