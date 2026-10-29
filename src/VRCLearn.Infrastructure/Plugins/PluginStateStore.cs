using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.Infrastructure.Plugins;

public sealed class PluginStateStore(AppPaths paths, ILogger<PluginStateStore> logger)
{
    private readonly Lock _gate = new();
    private PluginState? _state;

    public PluginState Current
    {
        get
        {
            lock (_gate)
            {
                return _state ??= JsonFileStore.Load<PluginState>(
                    paths.PluginStateFile,
                    message => logger.LogWarning("插件状态文件损坏：{Message}", message));
            }
        }
    }

    public bool IsEnabled(string id) => Current.Enabled.Contains(id, StringComparer.Ordinal);

    public void SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            var list = Current.Enabled;

            list.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));

            if (enabled)
            {
                list.Add(id);
            }

            Save();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            JsonFileStore.Save(
                paths.PluginStateFile,
                Current,
                message => logger.LogWarning("插件状态写入失败：{Message}", message));
        }
    }
}
