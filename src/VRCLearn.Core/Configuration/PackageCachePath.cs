namespace VRCLearn.Core.Configuration;

/// <summary>包缓存目录的解析与校验；清缓存会整目录删除缓存根下的子目录。</summary>
public static class PackageCachePath
{
    public static string Effective(GuiConfig config, AppPaths paths) =>
        string.IsNullOrWhiteSpace(config.CachePath) ? paths.RepositoriesDirectory : config.CachePath;

    /// <summary>数据目录、它的祖先、以及 vrclearn 自己那一层都不能当缓存目录。</summary>
    public static bool IsSafeDestination(AppPaths paths, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        return !PathUtil.SamePath(paths.Root, destination)
            && !PathUtil.IsUnder(paths.Root, destination)
            && !PathUtil.SamePath(paths.PrivateDirectory, destination)
            && !PathUtil.IsUnder(destination, paths.PrivateDirectory);
    }
}
