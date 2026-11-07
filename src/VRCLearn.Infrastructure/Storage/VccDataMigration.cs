using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.IO;
using VRCLearn.Core.Repositories;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Plugins;

namespace VRCLearn.Infrastructure.Storage;

/// <summary>
/// 从 %LOCALAPPDATA%\VRChatCreatorCompanion 一次性迁移到本程序的独立目录。
/// 这是升级兼容代码；存量用户完成迁移后，可以连同 GuiConfig.VccMigrationCompleted 和启动调用一起移除。
/// 包缓存与项目备份文件不复制，只把配置里的路径指向旧目录；插件、资源、模板等没有路径设置项的数据需要复制。
/// </summary>
public static class VccDataMigration
{
    private const string OldFolder = "VRChatCreatorCompanion";
    private const string OldPrivateFolder = "vrclearn";

    public static void Run(AppPaths paths, GuiConfigStore configs, ILogger logger)
    {
        if (!paths.IsDefaultRoot)
        {
            return;
        }

        var oldRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OldFolder);

        if (PathUtil.SamePath(oldRoot, paths.Root))
        {
            return;
        }

        try
        {
            Migrate(paths, oldRoot, configs, logger);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "从 VCC 目录迁移数据失败，下次启动重试");
        }
    }

    internal static void Migrate(AppPaths paths, string oldRoot, GuiConfigStore configs, ILogger logger)
    {
        var oldPrivate = Path.Combine(oldRoot, OldPrivateFolder);

        CopyFileIfMissing(Path.Combine(oldPrivate, "gui-config.json"), paths.GuiConfigFile, logger);

        var config = configs.Current;
        if (config.VccMigrationCompleted)
        {
            return;
        }

        var oldSettings = Path.Combine(oldRoot, "settings.json");
        if (!File.Exists(oldSettings))
        {
            config.VccMigrationCompleted = true;
            configs.Save();

            return;
        }

        var legacy = JsonFileStore.Peek<LauncherSettings>(oldSettings);
        if (legacy is null)
        {
            logger.LogWarning("读取旧的 settings.json 失败，本次不迁移，下次启动重试");

            return;
        }

        var copied = CopyRepositories(paths, oldRoot, legacy, logger)
            & CopyPrivateData(paths, oldPrivate, logger)
            & ClearPendingInstalls(paths, logger);

        if (string.IsNullOrWhiteSpace(config.CachePath))
        {
            config.CachePath = Path.Combine(oldRoot, AppPaths.RepositoriesFolderName);
        }

        if (string.IsNullOrWhiteSpace(legacy.ProjectBackupPath))
        {
            legacy.ProjectBackupPath = Path.Combine(oldPrivate, "Backup");
        }

        if (!File.Exists(paths.SettingsFile))
        {
            legacy.Rest.Clear();

            if (!JsonFileStore.Save(
                    paths.SettingsFile,
                    legacy,
                    message => logger.LogWarning("{Message}", message)))
            {
                logger.LogWarning("写入新的 settings.json 失败，本次不写完成标记，下次启动重试");

                return;
            }
        }

        config.VccMigrationCompleted = true;
        configs.Save();

        if (copied)
        {
            logger.LogInformation("已从 VCC 目录迁移配置到 {Root}", paths.Root);
        }
        else
        {
            logger.LogWarning("部分文件未能复制，已跳过；原文件仍在 {OldRoot}，可手动处理", oldRoot);
        }
    }

    private static bool CopyRepositories(
        AppPaths paths,
        string oldRoot,
        LauncherSettings legacy,
        ILogger logger)
    {
        var source = Path.Combine(oldRoot, AppPaths.RepositoriesFolderName);
        var copied = true;

        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(paths.RepositoriesDirectory);

            foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.TopDirectoryOnly))
            {
                copied &= CopyFileIfMissing(
                    file,
                    Path.Combine(paths.RepositoriesDirectory, Path.GetFileName(file)),
                    logger);
            }
        }

        for (var i = 0; i < legacy.UserRepos.Count; i++)
        {
            var repository = legacy.UserRepos[i];
            var path = Path.IsPathFullyQualified(repository.LocalPath)
                ? repository.LocalPath
                : Path.Combine(oldRoot, repository.LocalPath.Replace('/', Path.DirectorySeparatorChar));

            if (!PathUtil.IsUnder(path, source))
            {
                continue;
            }

            var name = Path.GetFileName(path);

            if (name.Length == 0)
            {
                continue;
            }

            if (!CopyFileIfMissing(path, Path.Combine(paths.RepositoriesDirectory, name), logger))
            {
                copied = false;

                var fallback = UserRepoSetting.Create(path, repository.Name, repository.Url, repository.Id);
                fallback.Headers = repository.Headers;
                legacy.UserRepos[i] = fallback;

                continue;
            }

            var migrated = UserRepoSetting.Create(
                $"{AppPaths.RepositoriesFolderName}/{name}",
                repository.Name,
                repository.Url,
                repository.Id);

            migrated.Headers = repository.Headers;
            legacy.UserRepos[i] = migrated;
        }

        return copied;
    }

    private static bool CopyPrivateData(AppPaths paths, string oldPrivate, ILogger logger)
    {
        var copied = true;

        foreach (var name in new[] { "resources.json", "plugin-state.json", "plugin-hub-state.json" })
        {
            copied &= CopyFileIfMissing(
                Path.Combine(oldPrivate, name),
                Path.Combine(paths.ConfigDirectory, name),
                logger);
        }

        copied &= CopyFileIfMissing(
            Path.Combine(oldPrivate, "update-state.json"),
            paths.UpdateStateFile,
            logger);

        foreach (var name in new[] { "resources", "plugins", "plugin-data", "templates" })
        {
            copied &= CopyTree(
                Path.Combine(oldPrivate, name),
                name switch
                {
                    "resources" => paths.ResourcesDirectory,
                    "plugins" => paths.PluginsDirectory,
                    "plugin-data" => paths.PluginDataDirectory,
                    _ => paths.TemplatesDirectory,
                },
                logger);
        }

        return copied;
    }

    private static bool ClearPendingInstalls(AppPaths paths, ILogger logger)
    {
        if (JsonFileStore.Peek<PluginState>(paths.PluginStateFile) is not { } state
            || state.PendingInstalls.Count == 0)
        {
            return true;
        }

        state.PendingInstalls.Clear();

        return JsonFileStore.Save(
            paths.PluginStateFile,
            state,
            message => logger.LogWarning("{Message}", message));
    }

    private static bool CopyTree(string source, string target, ILogger logger)
    {
        if (!Directory.Exists(source))
        {
            return true;
        }

        var copied = true;

        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", DirectoryEnumeration.Recursive))
            {
                var relative = Path.GetRelativePath(source, file);

                copied &= CopyFileIfMissing(file, Path.Combine(target, relative), logger);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "复制 {Source} 时失败", source);

            return false;
        }

        return copied;
    }

    private static bool CopyFileIfMissing(string source, string target, ILogger logger)
    {
        if (!File.Exists(source) || File.Exists(target))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "复制 {Source} 到 {Target} 失败", source, target);

            return false;
        }
    }
}
