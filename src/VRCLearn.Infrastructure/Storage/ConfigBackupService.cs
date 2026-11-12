using System.IO.Compression;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Resources;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.IO;

namespace VRCLearn.Infrastructure.Storage;

public sealed record ConfigBackupResult(int Files, long Bytes);

public sealed record ConfigRestoreResult(int Files, int Failed = 0);

public sealed class ConfigBackupService(
    AppPaths paths,
    ILauncherSettingsStore settings,
    IResourceStore resources,
    GuiConfigStore guiConfig)
{
    private const string CoversFolderName = "covers";

    private static readonly string[] TemplateExtensions = [".vrclearn-template", ".alcomtemplate"];

    public ConfigBackupResult Backup(string destinationPath)
    {
        var entries = Collect().ToList();

        if (entries.Count == 0)
        {
            return new ConfigBackupResult(0, 0);
        }

        if (Path.GetDirectoryName(destinationPath) is { Length: > 0 } directory)
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        var bytes = entries.Sum(x => new FileInfo(x.FullPath).Length);

        DiskSpace.Ensure(destinationPath, bytes);

        var temporary = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var (relative, fullPath) in entries)
                {
                    archive.CreateEntryFromFile(fullPath, relative, CompressionLevel.Optimal);
                }
            }

            File.Move(temporary, destinationPath, overwrite: true);
        }
        catch
        {
            FileCleanup.TryDelete(temporary);
            throw;
        }

        return new ConfigBackupResult(entries.Count, bytes);
    }

    public ConfigRestoreResult Restore(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);

        var targets = new List<(ZipArchiveEntry Entry, string Target)>();
        var rootBytes = 0L;

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            try
            {
                if (ZipEntryPath.Normalize(entry.FullName) is not { } relative || !IsConfigFile(relative))
                {
                    continue;
                }

                targets.Add((entry, ResolveArchivePath(relative)));
                rootBytes += entry.Length;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        DiskSpace.Ensure(paths.Root, rootBytes);

        var files = 0;
        var failed = 0;

        try
        {
            foreach (var (entry, target) in targets)
            {
                try
                {
                    if (Path.GetDirectoryName(target) is { Length: > 0 } directory)
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    entry.ExtractToFile(target, overwrite: true);
                    files++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failed++;
                }
            }

            RewriteResources();
        }
        finally
        {
            settings.Reload();
            resources.Reload();
            guiConfig.Reload();
        }

        return new ConfigRestoreResult(files, failed);
    }

    private IEnumerable<(string Relative, string FullPath)> Collect()
    {
        if (File.Exists(paths.SettingsFile))
        {
            yield return ("settings.json", paths.SettingsFile);
        }

        if (File.Exists(paths.GuiConfigFile))
        {
            yield return ("vrclearn/gui-config.json", paths.GuiConfigFile);
        }

        if (File.Exists(paths.PluginStateFile))
        {
            yield return ("vrclearn/plugin-state.json", paths.PluginStateFile);
        }

        foreach (var file in FilesIn(paths.TemplatesDirectory, TemplateExtensions))
        {
            yield return ($"vrclearn/templates/{Path.GetFileName(file)}", file);
        }

        if (File.Exists(paths.ResourcesFile))
        {
            yield return ("vrclearn/resources.json", paths.ResourcesFile);
        }

        foreach (var file in FilesIn(paths.RepositoriesDirectory, [".json"]))
        {
            yield return ($"Repos/{Path.GetFileName(file)}", file);
        }
    }

    private static IEnumerable<string> FilesIn(string directory, string[] extensions) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
                .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            : [];

    private void RewriteResources()
    {
        resources.Reload();

        var changed = false;

        foreach (var resource in resources.Current.Resources)
        {
            if (RelocatedCover(resource) is { } cover)
            {
                resource.CoverPath = cover;
                changed = true;
            }
        }

        if (changed)
        {
            resources.Save();
        }
    }

    private string? RelocatedCover(ResourceDefinition resource)
    {
        if (string.IsNullOrWhiteSpace(resource.CoverPath))
        {
            return null;
        }

        try
        {
            var name = Path.GetFileName(resource.CoverPath);

            if (name.Length == 0 || !Path.IsPathFullyQualified(resource.CoverPath))
            {
                return null;
            }

            var local = Path.Combine(paths.ResourceCoversDirectory, name);

            return File.Exists(local) ? $"{CoversFolderName}/{name}" : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private string ResolveArchivePath(string relative)
    {
        static string Rest(string value, string prefix) => value[prefix.Length..];

        if (relative.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
        {
            return paths.SettingsFile;
        }

        if (relative.StartsWith("vrclearn/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = Rest(relative, "vrclearn/");

            if (rest.Equals("gui-config.json", StringComparison.OrdinalIgnoreCase))
            {
                return paths.GuiConfigFile;
            }

            if (rest.Equals("plugin-state.json", StringComparison.OrdinalIgnoreCase))
            {
                return paths.PluginStateFile;
            }

            if (rest.Equals("resources.json", StringComparison.OrdinalIgnoreCase))
            {
                return paths.ResourcesFile;
            }

            if (rest.StartsWith("templates/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(
                    paths.TemplatesDirectory,
                    Rest(rest, "templates/").Replace('/', Path.DirectorySeparatorChar));
            }

            if (rest.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(
                    paths.ResourcesDirectory,
                    Rest(rest, "resources/").Replace('/', Path.DirectorySeparatorChar));
            }
        }

        if (relative.StartsWith($"{AppPaths.RepositoriesFolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                paths.RepositoriesDirectory,
                Rest(relative, $"{AppPaths.RepositoriesFolderName}/").Replace('/', Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException($"unexpected config file: {relative}");
    }

    private static bool IsConfigFile(string relative)
    {
        if (relative.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("vrclearn/gui-config.json", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("vrclearn/plugin-state.json", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("vrclearn/resources.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = relative.Split('/');

        if (segments.Length == 4
            && segments[0].Equals("vrclearn", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("resources", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("covers", StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(segments[3]).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (segments.Length == 2
            && segments[0].Equals(AppPaths.RepositoriesFolderName, StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(segments[1]).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return segments.Length == 3
            && segments[0].Equals("vrclearn", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("templates", StringComparison.OrdinalIgnoreCase)
            && TemplateExtensions.Contains(
                Path.GetExtension(segments[2]),
                StringComparer.OrdinalIgnoreCase);
    }
}
