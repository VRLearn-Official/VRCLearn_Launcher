namespace VRCLearn.Core.Configuration;

public sealed record AppPaths(string Root, bool IsDefaultRoot = false)
{
    private const string OverrideArgument = "--environment";
    private const string OverrideVariable = "VRCL_ENVIRONMENT";
    private const string DefaultFolder = "VRCLearnLauncher";
    private const string ConfigFolder = "config";
    private const string DataFolder = "data";
    private const string LogsFolder = "logs";

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

        return new AppPaths(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DefaultFolder),
            IsDefaultRoot: true);
    }

    public string ConfigDirectory => Path.Combine(Root, ConfigFolder);

    public string DataDirectory => Path.Combine(Root, DataFolder);

    public string LogsDirectory => Path.Combine(Root, LogsFolder);

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public const string RepositoriesFolderName = "Repos";

    /// <summary>仓库清单和包压缩包。</summary>
    public string RepositoriesDirectory => Path.Combine(DataDirectory, RepositoriesFolderName);

    public string GuiConfigFile => Path.Combine(ConfigDirectory, "gui-config.json");

    public string BackupDirectory => Path.Combine(DataDirectory, "Backup");

    public string ResourcesFile => Path.Combine(ConfigDirectory, "resources.json");

    public string ResourcesDirectory => Path.Combine(DataDirectory, "resources");

    public string ResourceCoversDirectory => Path.Combine(ResourcesDirectory, "covers");

    public string TemplatesDirectory => Path.Combine(DataDirectory, "templates");

    public string PluginsDirectory => Path.Combine(DataDirectory, "plugins");

    public string PluginDataDirectory => Path.Combine(DataDirectory, "plugin-data");

    public string PluginStateFile => Path.Combine(ConfigDirectory, "plugin-state.json");

    public string PluginHubStateFile => Path.Combine(ConfigDirectory, "plugin-hub-state.json");

    public string PluginUpdateTempDirectory => Path.Combine(DataDirectory, "plugin-updates");

    public string UpdateStateFile => Path.Combine(DataDirectory, "update-state.json");

    public string LauncherUpdateDirectory => Path.Combine(DataDirectory, "launcher-update");
}
