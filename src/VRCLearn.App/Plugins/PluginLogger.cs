using Microsoft.Extensions.Logging;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

internal sealed class PluginLogger(ILogger logger, string pluginId) : IPluginLogger
{
    public void Info(string message) =>
        logger.LogInformation("[{Plugin}] {Message}", pluginId, message);

    public void Warn(string message, Exception? exception = null) =>
        logger.LogWarning(exception, "[{Plugin}] {Message}", pluginId, message);

    public void Error(string message, Exception? exception = null) =>
        logger.LogError(exception, "[{Plugin}] {Message}", pluginId, message);
}
