using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Plugins;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

internal sealed class PluginContext : IPluginContext
{
    private readonly PluginLocalization _localization;
    private readonly PluginServiceRegistry _services;
    private readonly PluginWindowService _windows;
    private readonly IPluginLogger _logger;

    public PluginContext(
        PluginManifest manifest,
        string pluginDirectory,
        string dataDirectory,
        LocalizationService localization,
        PluginServiceRegistry services,
        PluginWindowService windows,
        ILogger logger)
    {
        PluginId = manifest.Id;
        PluginVersion = manifest.Version.ToString();
        HostVersion = PluginHostInfo.Version.ToString();
        DataDirectory = dataDirectory;
        _localization = new PluginLocalization(pluginDirectory, localization, logger);
        _services = services;
        _windows = windows;
        _logger = new PluginLogger(logger, manifest.Id);
    }

    public string PluginId { get; }

    public string PluginVersion { get; }

    public string DataDirectory { get; }

    public string HostVersion { get; }

    public string Language => _localization.Language;

    public IPluginLogger Logger => _logger;

    public IPluginLocalization Localization => _localization;

    public event EventHandler? LanguageChanged;

    public T? GetService<T>() where T : class => _services.Get<T>();

    public void RegisterService<T>(T service) where T : class =>
        _services.Register(PluginId, service);

    public void CloseWindows() => _windows.Close(PluginId);

    public void NotifyLanguageChanged()
    {
        _localization.Reload();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
