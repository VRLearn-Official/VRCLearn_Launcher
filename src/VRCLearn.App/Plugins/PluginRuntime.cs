using Microsoft.Extensions.Logging;
using VRCLearn.Infrastructure.Plugins;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

internal sealed class PluginRuntime(InstalledPlugin installed)
{
    public InstalledPlugin Installed { get; } = installed;

    public PluginLoadContext? LoadContext { get; set; }

    public IVclPlugin? Instance { get; set; }

    public PluginContext? Context { get; set; }

    public bool Faulted { get; set; }

    public string? Error { get; set; }

    public bool IsLoaded => Instance is not null;

    public async Task DisposeAsync(ILogger logger)
    {
        var instance = Instance;

        Instance = null;
        Context = null;

        var unload = true;

        try
        {
            if (instance is IAsyncDisposable asyncDisposable)
            {
                try
                {
                    await asyncDisposable
                        .DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(2))
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    unload = false;
                    logger.LogWarning("释放插件超时，跳过卸载：{Id}", Installed.Id);
                }
            }
            else if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "释放插件失败：{Id}", Installed.Id);
        }
        finally
        {
            if (unload)
            {
                LoadContext?.Unload();
            }
        }
    }
}
