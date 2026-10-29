namespace VRCLearn.Plugin.Sdk;

public interface IPluginLocalization
{
    string this[string key] { get; }

    string Format(string key, params object?[] arguments);
}
