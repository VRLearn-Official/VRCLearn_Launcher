namespace VRCLearn.Core.Plugins;

public sealed class PluginPackageException : Exception
{
    public PluginPackageException(string message)
        : base(message)
    {
    }
}
