using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Plugins;

namespace VRCLearn.Infrastructure.Plugins;

public sealed record InstalledPlugin(
    string Id,
    string Directory,
    PluginManifest? Manifest,
    string? Error);

public sealed class PluginDirectoryScanner(AppPaths paths, ILogger<PluginDirectoryScanner> logger)
{
    public IReadOnlyList<InstalledPlugin> Scan()
    {
        if (!Directory.Exists(paths.PluginsDirectory))
        {
            return [];
        }

        var plugins = new List<InstalledPlugin>();

        foreach (var directory in Directory.EnumerateDirectories(paths.PluginsDirectory))
        {
            var name = Path.GetFileName(directory);

            if (name.StartsWith('.') || name.StartsWith('_'))
            {
                continue;
            }

            plugins.Add(Read(name, directory));
        }

        return [.. plugins.OrderBy(x => x.Id, StringComparer.Ordinal)];
    }

    private InstalledPlugin Read(string name, string directory)
    {
        var manifestPath = Path.Combine(directory, PluginManifestReader.FileName);

        if (!File.Exists(manifestPath))
        {
            return new InstalledPlugin(name, directory, null, "plugin.json missing");
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            var manifest = PluginManifestReader.Read(stream);

            if (!string.Equals(manifest.Id, name, StringComparison.Ordinal))
            {
                return new InstalledPlugin(name, directory, null, "plugin id does not match folder name");
            }

            return new InstalledPlugin(name, directory, manifest, null);
        }
        catch (Exception exception) when (
            exception is PluginManifestException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "读取插件清单失败：{Path}", manifestPath);

            return new InstalledPlugin(name, directory, null, exception.Message);
        }
    }
}
