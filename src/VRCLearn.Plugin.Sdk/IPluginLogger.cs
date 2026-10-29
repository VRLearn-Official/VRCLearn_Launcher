namespace VRCLearn.Plugin.Sdk;

public interface IPluginLogger
{
    void Info(string message);

    void Warn(string message, Exception? exception = null);

    void Error(string message, Exception? exception = null);
}
