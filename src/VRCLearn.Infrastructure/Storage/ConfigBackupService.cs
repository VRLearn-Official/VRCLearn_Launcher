using System.IO.Compression;
using System.Text.Json;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Resources;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.IO;

namespace VRCLearn.Infrastructure.Storage;

public enum BackupScope
{
    Config,

    Full,
}

public sealed record ConfigBackupResult(int Files, long Bytes);

public sealed record ConfigRestoreResult(int Files, int Failed = 0);

public sealed class ConfigBackupService(
    AppPaths paths,
    ILauncherSettingsStore settings,
    IResourceStore resources,
    GuiConfigStore guiConfig)
{
    public const string ManagedFolderName = "vrclearn_launcher_managed";

    private const string ManifestEntry = "backup-manifest.json";

    private const string ResourcesEntryPrefix = "vrclearn/resources/";

    private const string CoversEntryPrefix = "vrclearn/resources/covers/";

    private const string UnityPackagesEntryPrefix = "unitypackages/";

    private const string ManualsEntryPrefix = "manuals/";

    private const string CoversFolderName = "covers";

    private const string ProjectsEntryPrefix = "projects/";

    private static readonly string[] TemplateExtensions = [".vrclearn-template", ".alcomtemplate"];

    public bool IsFullBackup(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);

        return ReadManifest(archive) is not null;
    }

    public ConfigBackupResult Backup(string destinationPath, BackupScope scope = BackupScope.Config)
    {
        var manifest = new BackupManifest { Scope = scope == BackupScope.Full ? "full" : "config" };
        var staging = scope == BackupScope.Full ? StageResourceFiles(manifest) : null;

        try
        {
            var directories = new List<string>();
            var entries = Collect(scope, manifest, staging?.Files ?? [], directories)
                .Where(x => File.Exists(x.FullPath))
                .ToList();

            if (entries.Count == 0 && directories.Count == 0)
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
                    foreach (var projectDirectory in directories)
                    {
                        archive.CreateEntry($"{projectDirectory}/");
                    }

                    foreach (var (relative, fullPath) in entries)
                    {
                        archive.CreateEntryFromFile(fullPath, relative, CompressionLevel.Optimal);
                    }

                    if (scope == BackupScope.Full)
                    {
                        using var stream = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open();

                        JsonSerializer.Serialize(stream, manifest, JsonDefaults.Indented);
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
        finally
        {
            if (staging is not null)
            {
                TryDeleteDirectory(staging.Directory);
            }
        }
    }

    /// <param name="managedRoot">用户指定的目录；全量包的项目与 unitypackage 会放进其下的 managed 目录。</param>
    public ConfigRestoreResult Restore(string sourcePath, string? managedRoot = null)
    {
        using var archive = ZipFile.OpenRead(sourcePath);

        var manifest = ReadManifest(archive);
        var plan = BuildPlan(manifest, managedRoot);

        var targets = new List<(ZipArchiveEntry Entry, string Target)>();
        var directoryTargets = new List<string>();
        var rootBytes = 0L;
        var managedBytes = 0L;

        foreach (var entry in archive.Entries)
        {
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

            try
            {
                if (ZipEntryPath.Normalize(entry.FullName) is not { } relative)
                {
                    continue;
                }

                if (isDirectory)
                {
                    if (relative.StartsWith(ProjectsEntryPrefix, StringComparison.OrdinalIgnoreCase)
                        && TryResolve(relative, manifest, plan, out var directoryTarget))
                    {
                        directoryTargets.Add(directoryTarget);
                    }

                    continue;
                }

                if (!TryResolve(relative, manifest, plan, out var target))
                {
                    continue;
                }

                targets.Add((entry, target));

                if (plan.ManagedRoot is not null && IsUnder(target, plan.ManagedRoot))
                {
                    managedBytes += entry.Length;
                }
                else
                {
                    rootBytes += entry.Length;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        DiskSpace.Ensure(paths.Root, rootBytes);

        if (plan.ManagedRoot is not null)
        {
            DiskSpace.Ensure(plan.ManagedRoot, managedBytes);
        }

        var files = 0;
        var failed = 0;

        try
        {
            foreach (var projectDirectory in directoryTargets)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(projectDirectory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

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

            if (manifest is not null)
            {
                RewriteProjects(manifest, plan.ProjectTargets);

                if (plan.ManagedRoot is not null)
                {
                    RewriteManagedDirectory(plan.ManagedRoot);
                }
            }

            RewriteResources(manifest, plan.ManagedRoot);
        }
        finally
        {
            settings.Reload();
            resources.Reload();
            guiConfig.Reload();
        }

        return new ConfigRestoreResult(files, failed);
    }

    private IEnumerable<(string Relative, string FullPath)> Collect(
        BackupScope scope,
        BackupManifest manifest,
        IReadOnlyList<(string Relative, string FullPath)> staged,
        List<string> directories)
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

        foreach (var file in FilesIn(Path.Combine(paths.PrivateDirectory, "templates"), TemplateExtensions))
        {
            yield return ($"vrclearn/templates/{Path.GetFileName(file)}", file);
        }

        if (File.Exists(paths.ResourcesFile))
        {
            yield return ("vrclearn/resources.json", paths.ResourcesFile);
        }

        if (scope == BackupScope.Config)
        {
            foreach (var file in FilesIn(paths.RepositoriesDirectory, [".json"]))
            {
                yield return ($"Repos/{Path.GetFileName(file)}", file);
            }

            yield break;
        }

        foreach (var file in FilesInTree(paths.ResourcesDirectory))
        {
            yield return ($"{ResourcesEntryPrefix}{Relative(file, paths.ResourcesDirectory)}", file);
        }

        foreach (var cover in ReferencedCovers(paths.ResourcesDirectory))
        {
            yield return cover;
        }

        foreach (var file in FilesInTree(paths.RepositoriesDirectory))
        {
            yield return ($"Repos/{Relative(file, paths.RepositoriesDirectory)}", file);
        }

        foreach (var stagedFile in staged)
        {
            yield return stagedFile;
        }

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in settings.Current.UserProjects)
        {
            if (!Path.IsPathFullyQualified(project) || !System.IO.Directory.Exists(project))
            {
                continue;
            }

            var index = manifest.Projects.Count;

            manifest.Projects.Add(project);

            var content = ProjectFiles.Collect(
                new PhysicalFileIo(project),
                ProjectFileFilter.ForBackup());

            foreach (var directory in content.Directories)
            {
                directories.Add($"{ProjectsEntryPrefix}{index}/{directory}");
            }

            foreach (var relative in content.Files)
            {
                var file = Path.Combine(project, relative.Replace('/', Path.DirectorySeparatorChar));

                if (added.Add(file))
                {
                    yield return ($"{ProjectsEntryPrefix}{index}/{relative}", file);
                }
            }
        }
    }

    private IEnumerable<(string Relative, string FullPath)> ReferencedCovers(string includedRoot)
    {
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources.Current.Resources)
        {
            var path = resources.CoverPath(resource);

            if (path.Length == 0
                || IsUnder(path, includedRoot)
                || !File.Exists(path)
                || !added.Add(path))
            {
                continue;
            }

            yield return ($"{CoversEntryPrefix}{Path.GetFileName(path)}", path);
        }
    }

    private Staging? StageResourceFiles(BackupManifest manifest)
    {
        var root = Path.Combine(
            paths.PrivateDirectory,
            $".backup-staging-{Guid.NewGuid():N}");

        var files = new List<(string Relative, string FullPath)>();

        try
        {
            var index = 0;
            var manualIndex = 0;

            foreach (var resource in resources.Current.Resources)
            {
                if (StageManual(resource, manualIndex, root, files, manifest))
                {
                    manualIndex++;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = new List<string>();

                foreach (var path in resource.UnityPackagePaths)
                {
                    if (!Path.IsPathFullyQualified(path)
                        || !File.Exists(path)
                        || !seen.Add(path))
                    {
                        continue;
                    }

                    var name = $"{entries.Count:000}-{Path.GetFileName(path)}";
                    var relative = $"{UnityPackagesEntryPrefix}{index}/{name}";
                    var destination = Path.Combine(root, index.ToString(), name);

                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(path, destination, overwrite: true);

                    files.Add((relative, destination));
                    entries.Add(relative);
                }

                if (entries.Count > 0)
                {
                    manifest.ResourcePackages[resource.Id] = entries;
                    index++;
                }
            }
        }
        catch
        {
            TryDeleteDirectory(root);
            throw;
        }

        return new Staging(root, files);
    }

    private static bool StageManual(
        ResourceDefinition resource,
        int index,
        string root,
        ICollection<(string Relative, string FullPath)> files,
        BackupManifest manifest)
    {
        var manual = resource.Manual.Trim();

        if (manual.Length == 0
            || ResourceManual.IsUrl(manual)
            || !Path.IsPathFullyQualified(manual)
            || !File.Exists(manual)
            || Path.GetFileName(manual) is not { Length: > 0 } name)
        {
            return false;
        }

        var relative = $"{ManualsEntryPrefix}{index}/{name}";
        var destination = Path.Combine(root, ManualsEntryPrefix, index.ToString(), name);

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(manual, destination, overwrite: true);

        files.Add((relative, destination));
        manifest.Manuals[resource.Id] = relative;

        return true;
    }

    private void RewriteProjects(BackupManifest manifest, IReadOnlyList<string> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < manifest.Projects.Count && index < targets.Count; index++)
        {
            if (!System.IO.Directory.Exists(targets[index]))
            {
                continue;
            }

            var key = Normalized(manifest.Projects[index]);

            if (key.Length > 0)
            {
                map[key] = targets[index];
            }
        }

        if (map.Count == 0)
        {
            return;
        }

        settings.Reload();

        var current = settings.Current;
        var changed = false;

        for (var index = 0; index < current.UserProjects.Count; index++)
        {
            if (map.TryGetValue(Normalized(current.UserProjects[index]), out var target))
            {
                current.UserProjects[index] = target;
                changed = true;
            }
        }

        if (changed)
        {
            settings.Save();
        }
    }

    private void RewriteResources(BackupManifest? manifest, string? managedRoot)
    {
        resources.Reload();

        var collection = resources.Current;
        var changed = false;

        foreach (var resource in collection.Resources)
        {
            if (manifest is not null
                && managedRoot is not null
                && manifest.ResourcePackages.TryGetValue(resource.Id, out var entries))
            {
                var restored = entries
                    .Select(x => Path.Combine(managedRoot, x.Replace('/', Path.DirectorySeparatorChar)))
                    .Where(x => IsExistingFileUnder(x, managedRoot))
                    .ToList();

                if (restored.Count > 0)
                {
                    resource.UnityPackagePaths = restored;
                    changed = true;
                }
            }

            if (manifest is not null
                && managedRoot is not null
                && manifest.Manuals.TryGetValue(resource.Id, out var manualEntry))
            {
                var manual = Path.Combine(managedRoot, manualEntry.Replace('/', Path.DirectorySeparatorChar));

                if (IsExistingFileUnder(manual, managedRoot))
                {
                    resource.Manual = manual;
                    changed = true;
                }
            }

            if (RelocatedCover(resource, managedRoot) is { } cover)
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

    private string? RelocatedCover(ResourceDefinition resource, string? managedRoot)
    {
        if (string.IsNullOrWhiteSpace(resource.CoverPath))
        {
            return null;
        }

        try
        {
            var name = Path.GetFileName(resource.CoverPath);

            if (name.Length == 0)
            {
                return null;
            }

            if (managedRoot is not null)
            {
                var managed = Path.Combine(managedRoot, CoversFolderName, name);

                if (IsExistingFileUnder(managed, managedRoot))
                {
                    return managed;
                }
            }

            if (!Path.IsPathFullyQualified(resource.CoverPath))
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

    private void RewriteManagedDirectory(string managedRoot)
    {
        guiConfig.Reload();

        guiConfig.Current.ManagedDataDirectory = managedRoot;
        guiConfig.Save();
    }

    private RestorePlan BuildPlan(BackupManifest? manifest, string? managedRoot)
    {
        if (manifest is null || managedRoot is null || !Path.IsPathFullyQualified(managedRoot))
        {
            return new RestorePlan(null, []);
        }

        var managed = Path.Combine(Path.GetFullPath(managedRoot), ManagedFolderName);
        var projectsRoot = Path.Combine(managed, "projects");
        var targets = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < manifest.Projects.Count; index++)
        {
            var name = SafeProjectName(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(manifest.Projects[index])),
                index);

            var target = Path.Combine(projectsRoot, name);
            var suffix = 2;

            while (!used.Add(target))
            {
                target = Path.Combine(projectsRoot, $"{name} ({suffix++})");
            }

            targets.Add(target);
        }

        return new RestorePlan(managed, targets);
    }

    private bool TryResolve(
        string relative,
        BackupManifest? manifest,
        RestorePlan plan,
        out string target)
    {
        target = "";

        if (manifest is not null
            && plan.ManagedRoot is not null
            && relative.StartsWith(CoversEntryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = relative[CoversEntryPrefix.Length..];
            var coverPath = Path.Combine(
                plan.ManagedRoot,
                CoversFolderName,
                name.Replace('/', Path.DirectorySeparatorChar));

            if (name.Length == 0 || !IsUnder(coverPath, plan.ManagedRoot))
            {
                return false;
            }

            target = coverPath;
            return true;
        }

        if (manifest is not null
            && plan.ManagedRoot is not null
            && relative.StartsWith(ManualsEntryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var manualPath = Path.Combine(
                plan.ManagedRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));

            if (!IsUnder(manualPath, plan.ManagedRoot))
            {
                return false;
            }

            target = manualPath;
            return true;
        }

        if (IsConfigFile(relative) || (manifest is not null && IsLauncherData(relative)))
        {
            target = Path.Combine(paths.Root, relative.Replace('/', Path.DirectorySeparatorChar));
            return true;
        }

        if (manifest is null || plan.ManagedRoot is null)
        {
            return false;
        }

        if (TryResolveProject(relative, plan, out target))
        {
            return true;
        }

        if (!relative.StartsWith(UnityPackagesEntryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = Path.Combine(plan.ManagedRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        if (!IsUnder(path, plan.ManagedRoot))
        {
            return false;
        }

        target = path;
        return true;
    }

    private static bool TryResolveProject(string relative, RestorePlan plan, out string target)
    {
        target = "";

        var segments = relative.Split('/');

        if (segments.Length < 3
            || !segments[0].Equals("projects", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(segments[1], out var index)
            || index < 0
            || index >= plan.ProjectTargets.Count)
        {
            return false;
        }

        var root = plan.ProjectTargets[index];
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments[2..])));

        if (!IsUnder(path, root))
        {
            return false;
        }

        target = path;
        return true;
    }

    private static BackupManifest? ReadManifest(ZipArchive archive)
    {
        if (archive.GetEntry(ManifestEntry) is not { } entry)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();

            var manifest = JsonSerializer.Deserialize<BackupManifest>(stream, JsonDefaults.Compact);

            if (manifest is null)
            {
                return null;
            }

            manifest.Projects ??= [];
            manifest.ResourcePackages ??= [];
            manifest.Manuals ??= [];

            return manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> FilesIn(string directory, string[] extensions)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var extension in extensions)
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, $"*{extension}"))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> FilesInTree(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            yield break;
        }

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*", options))
        {
            yield return file;
        }
    }

    private static string Relative(string file, string root) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string Normalized(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    private static string SafeProjectName(string name, int index)
    {
        if (name is "" or "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name[^1] is '.' or ' ')
        {
            return $"project-{index + 1}";
        }

        return name;
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExistingFileUnder(string path, string root)
    {
        try
        {
            return IsUnder(path, root) && File.Exists(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsLauncherData(string relative)
    {
        var segments = relative.Split('/');

        return (segments.Length >= 2
                && segments[0].Equals(AppPaths.RepositoriesFolderName, StringComparison.OrdinalIgnoreCase))
            || (segments.Length >= 3
                && segments[0].Equals("vrclearn", StringComparison.OrdinalIgnoreCase)
                && segments[1].Equals("resources", StringComparison.OrdinalIgnoreCase));
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

    private sealed record RestorePlan(string? ManagedRoot, IReadOnlyList<string> ProjectTargets);

    private sealed record Staging(string Directory, IReadOnlyList<(string Relative, string FullPath)> Files);

    private sealed class BackupManifest
    {
        public string Scope { get; set; } = "";

        public List<string> Projects { get; set; } = [];

        public Dictionary<string, List<string>> ResourcePackages { get; set; } = [];

        public Dictionary<string, string> Manuals { get; set; } = [];
    }
}
