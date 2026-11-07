namespace VRCLearn.Core.Configuration;

/// <summary>包缓存目录的解析与校验；清缓存会整目录删除缓存根下的子目录。</summary>
public static class PackageCachePath
{
    public static string Effective(GuiConfig config, AppPaths paths) =>
        string.IsNullOrWhiteSpace(config.CachePath) ? paths.RepositoriesDirectory : config.CachePath;

    /// <summary>数据根、配置目录、数据目录以及它们的祖先都不能当缓存目录。</summary>
    public static bool IsSafeDestination(AppPaths paths, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        return !PathUtil.SamePath(paths.Root, destination)
            && !PathUtil.IsUnder(paths.Root, destination)
            && !PathUtil.SamePath(paths.ConfigDirectory, destination)
            && !PathUtil.IsUnder(paths.ConfigDirectory, destination)
            && !PathUtil.IsUnder(destination, paths.ConfigDirectory)
            && !PathUtil.SamePath(paths.DataDirectory, destination)
            && !PathUtil.IsUnder(paths.DataDirectory, destination)
            && !Touches(paths.ResourcesDirectory, destination)
            && !Touches(paths.PluginsDirectory, destination)
            && !Touches(paths.PluginDataDirectory, destination)
            && !Touches(paths.PluginUpdateTempDirectory, destination)
            && !Touches(paths.TemplatesDirectory, destination)
            && !Touches(paths.BackupDirectory, destination);
    }

    private static bool Touches(string directory, string destination) =>
        PathUtil.SamePath(directory, destination) || PathUtil.IsUnder(destination, directory);
}
