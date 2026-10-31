using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace VRCLearn.App.Plugins;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _directory;
    private readonly AssemblyDependencyResolver? _resolver;

    public PluginLoadContext(string directory, string entryAssemblyPath)
        : base($"VclPlugin:{Path.GetFileName(directory)}", isCollectible: true)
    {
        _directory = directory;

        if (File.Exists(entryAssemblyPath))
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "VRCLearn.Plugin.Sdk")
        {
            return typeof(VRCLearn.Plugin.Sdk.IVclPlugin).Assembly;
        }

        if (assemblyName.Name is not { } name || IsHostAssembly(name))
        {
            return null;
        }

        if (_resolver?.ResolveAssemblyToPath(assemblyName) is { } resolved)
        {
            return LoadFromAssemblyPath(resolved);
        }

        var candidate = Path.Combine(_directory, $"{name}.dll");

        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);

        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    private static bool IsHostAssembly(string name) =>
        name is "VRCLearn.Core"
            or "VRCLearn.Infrastructure"
            or "VRCLearnLauncher"
            or "PresentationFramework"
            or "PresentationCore"
            or "WindowsBase"
            or "System.Xaml"
            or "System"
            or "netstandard"
            or "mscorlib"
            or "Wpf.Ui"
            or "Wpf.Ui.Abstractions"
            or "CommunityToolkit.Mvvm"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);
}
