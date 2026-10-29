namespace VRCLearn.Core.Configuration;

public sealed record AppPaths(string Root)
{
    private const string OverrideArgument = "--environment";
    private const string OverrideVariable = "VRCL_ENVIRONMENT";
    private const string PrivateFolder = "vrclearn";

    private const string VccFolder = "VRChatCreatorCompanion";

    public static AppPaths Resolve(string[] args)
    {
        var index = Array.IndexOf(args, OverrideArgument);
        if (index >= 0 && index + 1 < args.Length)
        {
            return new AppPaths(Path.GetFullPath(args[index + 1]));
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new AppPaths(Path.GetFullPath(fromEnvironment));
        }

        return new AppPaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VccFolder));
    }

    public string PrivateDirectory => Path.Combine(Root, PrivateFolder);

    // ---- 与 VCC 共用 ----

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public const string RepositoriesFolderName = "Repos";

    /// <summary>仓库清单和包压缩包。</summary>
    public string RepositoriesDirectory => Path.Combine(Root, RepositoriesFolderName);

    // ---- 我们自己的 ----

    public string GuiConfigFile => Path.Combine(PrivateDirectory, "gui-config.json");
    public string BackupDirectory => Path.Combine(PrivateDirectory, "Backup");
    public string LogsDirectory => Path.Combine(PrivateDirectory, "Logs");

    // ---- 资源库 ----

    public string ResourcesFile => Path.Combine(PrivateDirectory, "resources.json");

    public string ResourcesDirectory => Path.Combine(PrivateDirectory, "resources");

    public string ResourceCoversDirectory => Path.Combine(ResourcesDirectory, "covers");

    // ---- 插件 ----

    public string PluginsDirectory => Path.Combine(PrivateDirectory, "plugins");

    public string PluginDataDirectory => Path.Combine(PrivateDirectory, "plugin-data");

    public string PluginStateFile => Path.Combine(PrivateDirectory, "plugin-state.json");
}
