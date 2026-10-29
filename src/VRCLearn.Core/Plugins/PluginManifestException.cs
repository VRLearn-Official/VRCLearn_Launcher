namespace VRCLearn.Core.Plugins;

public sealed class PluginManifestException : Exception
{
    public PluginManifestException(string message)
        : base(message)
    {
    }

    public PluginManifestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
