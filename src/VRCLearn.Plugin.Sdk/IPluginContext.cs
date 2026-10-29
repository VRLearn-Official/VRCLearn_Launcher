namespace VRCLearn.Plugin.Sdk;

public interface IPluginContext
{
    string PluginId { get; }

    string PluginVersion { get; }

    string DataDirectory { get; }

    string HostVersion { get; }

    string Language { get; }

    IPluginLogger Logger { get; }

    IPluginLocalization Localization { get; }

    T? GetService<T>() where T : class;

    void RegisterService<T>(T service) where T : class;

    void CloseWindows();

    event EventHandler? LanguageChanged;
}
