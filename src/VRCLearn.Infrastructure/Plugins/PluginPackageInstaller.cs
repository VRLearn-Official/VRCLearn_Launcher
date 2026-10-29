using Microsoft.Extensions.Logging;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Plugins;
using VRCLearn.Infrastructure.IO;

namespace VRCLearn.Infrastructure.Plugins;

public enum PluginInstallOutcome
{
    Installed,

    PendingRestart,
}

public sealed record PluginInstallResult(
    PluginManifest Manifest,
    string Sha256,
    PluginInstallOutcome Outcome,
    bool Replaced);

public enum PluginUninstallOutcome
{
    Removed,

    PendingRestart,
}

public sealed record PluginUninstallResult(PluginUninstallOutcome Outcome, bool DataRemoved);

public sealed class PluginPackageInstaller(
    AppPaths paths,
    PluginStateStore states,
    ILogger<PluginPackageInstaller> logger)
{
    public const string Extension = ".vclplugin";

    private const long MaxPackageBytes = 512L * 1024 * 1024;

    public async Task<(PluginManifest Manifest, string Sha256)> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(packagePath);
        var inspection = PluginPackageInspector.Inspect(stream);
        var sha256 = await PluginPackageInspector
            .ComputeSha256Async(packagePath, cancellationToken);

        return (inspection.Manifest, sha256);
    }

    public async Task<PluginInstallResult> InstallAsync(
        string packagePath,
        bool isLoaded,
        CancellationToken cancellationToken,
        string? expectedSha256 = null)
    {
        var file = new FileInfo(packagePath);

        if (!file.Exists)
        {
            throw new FileNotFoundException("plugin package not found", packagePath);
        }

        if (file.Length > MaxPackageBytes)
        {
            throw new PluginPackageException($"plugin package is too large: {file.Length} bytes");
        }

        await using var stream = File.OpenRead(packagePath);

        var inspection = PluginPackageInspector.Inspect(stream);
        var manifest = inspection.Manifest;

        var compatibility = PluginCompatibility.Check(
            manifest,
            PluginHostInfo.Version,
            PluginHostInfo.ApiVersion);

        if (compatibility.Count > 0)
        {
            throw new PluginPackageException(string.Join("; ", compatibility));
        }

        var sha256 = await PluginPackageInspector
            .ComputeSha256Async(packagePath, cancellationToken);

        if (!string.IsNullOrEmpty(expectedSha256)
            && !string.Equals(sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginPackageException(
                "plugin package content changed after confirmation");
        }

        Directory.CreateDirectory(paths.PluginsDirectory);

        var target = Path.Combine(paths.PluginsDirectory, manifest.Id);
        var stagingName = $".installing-{Guid.NewGuid():N}";
        var staging = Path.Combine(paths.PluginsDirectory, stagingName);

        try
        {
            stream.Position = 0;

            await SafeZipExtractor.ExtractAsync(
                stream,
                new PhysicalFileIo(paths.PluginsDirectory),
                stagingName,
                cancellationToken);

            var entryPath = Path.Combine(
                staging,
                manifest.Entry.Assembly.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(entryPath))
            {
                throw new PluginPackageException(
                    $"entry assembly is missing from the package: {manifest.Entry.Assembly}");
            }
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }

        if (isLoaded && Directory.Exists(target))
        {
            return StagePendingInstall(manifest, sha256, staging, target);
        }

        var replaced = Directory.Exists(target);

        try
        {
            if (replaced)
            {
                ReplaceDirectory(target, staging, manifest.Id);
            }
            else
            {
                Directory.Move(staging, target);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "替换插件失败，改为下次启动安装：{Id}", manifest.Id);

            return StagePendingInstall(manifest, sha256, staging, target);
        }

        RemovePendingInstall(manifest.Id);

        if (!replaced)
        {
            states.Current.Enabled.RemoveAll(x => string.Equals(x, manifest.Id, StringComparison.Ordinal));
        }

        states.Save();

        return new PluginInstallResult(manifest, sha256, PluginInstallOutcome.Installed, replaced);
    }

    public PluginUninstallResult Uninstall(string id, bool deleteData)
    {
        var target = Path.Combine(paths.PluginsDirectory, id);
        var restart = false;

        states.Current.Enabled.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));

        if (states.Current.PendingInstalls.Remove(id, out var pending))
        {
            TryDeleteDirectory(pending);
        }

        if (Directory.Exists(target))
        {
            var removed = Path.Combine(
                paths.PluginsDirectory,
                $".removed-{id}-{Guid.NewGuid():N}");

            try
            {
                Directory.Move(target, removed);
                TryDeleteDirectory(removed);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "插件目录被占用，下次启动再删除：{Id}", id);

                if (!states.Current.PendingRemovals.Contains(id, StringComparer.Ordinal))
                {
                    states.Current.PendingRemovals.Add(id);
                }

                restart = true;
            }
        }

        if (!restart)
        {
            states.Current.PendingRemovals.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
        }

        var dataRemoved = false;

        if (deleteData)
        {
            var data = Path.Combine(paths.PluginDataDirectory, id);

            if (Directory.Exists(data))
            {
                dataRemoved = TryDeleteDirectory(data);
            }
        }

        states.Save();

        return new PluginUninstallResult(
            restart ? PluginUninstallOutcome.PendingRestart : PluginUninstallOutcome.Removed,
            dataRemoved);
    }

    public void ApplyPendingActions()
    {
        Directory.CreateDirectory(paths.PluginsDirectory);

        foreach (var id in states.Current.PendingRemovals.ToList())
        {
            if (!PathUtil.IsSafePathSegment(id))
            {
                states.Current.PendingRemovals.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
                continue;
            }

            var target = Path.Combine(paths.PluginsDirectory, id);

            if (TryDeleteDirectory(target))
            {
                states.Current.PendingRemovals.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
                states.Current.Enabled.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
            }
        }

        foreach (var (id, pending) in states.Current.PendingInstalls.ToList())
        {
            if (!PathUtil.IsSafePathSegment(id)
                || !PathUtil.IsUnder(pending, paths.PluginsDirectory)
                || !Path.GetFileName(pending).StartsWith(".pending-", StringComparison.Ordinal)
                || !Directory.Exists(pending))
            {
                states.Current.PendingInstalls.Remove(id);
                continue;
            }

            var target = Path.Combine(paths.PluginsDirectory, id);
            var old = Path.Combine(paths.PluginsDirectory, $".old-{id}-{Guid.NewGuid():N}");

            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Move(target, old);
                }

                Directory.Move(pending, target);
                TryDeleteDirectory(old);
                states.Current.PendingInstalls.Remove(id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "应用待安装插件失败：{Id}", id);

                if (Directory.Exists(old) && !Directory.Exists(target))
                {
                    try
                    {
                        Directory.Move(old, target);
                    }
                    catch (Exception restoreException) when (
                        restoreException is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(restoreException, "恢复旧插件目录失败：{Id}", id);
                    }
                }
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(paths.PluginsDirectory))
        {
            var name = Path.GetFileName(directory);

            if (name.StartsWith(".installing-", StringComparison.Ordinal)
                || name.StartsWith(".old-", StringComparison.Ordinal)
                || name.StartsWith(".removed-", StringComparison.Ordinal)
                || (name.StartsWith(".pending-", StringComparison.Ordinal)
                    && !states.Current.PendingInstalls.Values.Contains(
                        directory,
                        StringComparer.OrdinalIgnoreCase)))
            {
                TryDeleteDirectory(directory);
            }
        }

        states.Save();
    }

    private PluginInstallResult StagePendingInstall(
        PluginManifest manifest,
        string sha256,
        string staging,
        string target)
    {
        if (!Directory.Exists(staging))
        {
            throw new DirectoryNotFoundException($"plugin staging directory is missing: {staging}");
        }

        var pending = Path.Combine(
            paths.PluginsDirectory,
            $".pending-{manifest.Id}-{Guid.NewGuid():N}");

        Directory.Move(staging, pending);

        if (states.Current.PendingInstalls.Remove(manifest.Id, out var previous))
        {
            TryDeleteDirectory(previous);
        }

        states.Current.PendingInstalls[manifest.Id] = pending;
        states.Save();

        return new PluginInstallResult(
            manifest,
            sha256,
            PluginInstallOutcome.PendingRestart,
            Directory.Exists(target));
    }

    private void RemovePendingInstall(string id)
    {
        if (states.Current.PendingInstalls.Remove(id, out var pending))
        {
            TryDeleteDirectory(pending);
        }
    }

    private static void ReplaceDirectory(string target, string staging, string id)
    {
        var old = Path.Combine(Path.GetDirectoryName(target)!, $".old-{id}-{Guid.NewGuid():N}");

        Directory.Move(target, old);

        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            Directory.Move(old, target);
            throw;
        }

        TryDeleteDirectory(old);
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
