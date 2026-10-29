using System.Text.Json.Serialization;
using VRCLearn.Core.Projects;

namespace VRCLearn.Core.Configuration;

public sealed class GuiConfig : IJsonOnDeserialized
{
    public string Theme { get; set; } = "System";

    public string Language { get; set; } = "";

    /// <summary><c>diamond</c>（默认）/ <c>bilibili</c> / <c>grass</c> / <c>system</c>。</summary>
    public string ButtonColor { get; set; } = "diamond";

    public bool AnimationEnabled { get; set; } = true;

    public bool ExperimentalFeatures { get; set; }

    public bool ProxyEnabled { get; set; }

    public string ProxyUrl { get; set; } = "";

    /// <summary><c>default</c> / <c>zip-store</c> / <c>zip-best</c>。</summary>
    public string BackupFormat { get; set; } = "default";

    public bool UseForVccProtocol { get; set; }

    public bool UseForAlcomTemplateFiles { get; set; }

    /// <summary><c>inherit</c>（已继承 ALCOM 模板）/ <c>fresh</c>（选择全新开始）/ 空表示还没问过。</summary>
    public string AlcomTemplateChoice { get; set; } = "";

    public bool SetupCompleted { get; set; }

    /// <summary><c>name</c> / <c>type</c> / <c>unity</c> / <c>createdAt</c> / <c>lastModified</c>，可再跟 <c>Reversed</c> 表示倒序。</summary>
    public string ProjectSorting { get; set; } = "lastModified";

    /// <summary><c>name</c> / <c>installed</c> / <c>enabled</c>。</summary>
    public string PluginsSorting { get; set; } = "name";

    /// <summary><c>newest</c> / <c>oldest</c>。</summary>
    public string BackupsSorting { get; set; } = "newest";

    /// <summary><c>backup</c> / <c>archive</c>。</summary>
    public string BackupsFilter { get; set; } = "backup";


    /// <summary>取值是 <c>Microsoft.Extensions.Logging.LogLevel</c> 的名字。</summary>
    public string LogsLevel { get; set; } = "Information";

    /// <summary>日志保留份数，<c>0</c> 表示不删。</summary>
    public int LogsRetentionCount { get; set; } = 30;

    /// <summary>藏起来的仓库 id，照常更新与解析，只是不出现在可安装列表。</summary>
    public List<string> HiddenRepositories { get; set; } = [];

    public List<string> DefaultUnityArguments { get; set; } = [];

    /// <summary>按项目目录覆盖的 Unity 启动参数。</summary>
    public Dictionary<string, List<string>> ProjectUnityArguments { get; set; } = [];

    /// <summary><c>read-config</c>（读 <c>editors-v2.json</c>，默认）或 <c>call-hub</c>（起一个 headless 的 Hub）。</summary>
    public string UnityHubAccessMethod { get; set; } = "read-config";

    /// <summary>收藏的模板 id，排在列表前面。</summary>
    public List<string> FavoriteTemplates { get; set; } = [];

    /// <summary>收藏的项目目录，排在列表前面。</summary>
    public List<string> FavoriteProjects { get; set; } = [];

    public List<BackupRecord> BackupRecords { get; set; } = [];

    public bool AutoBackupEnabled { get; set; }

    public int AutoBackupIntervalMinutes { get; set; } = 30;

    public int AutoBackupRetention { get; set; } = 5;

    public bool HideLocalUserPackages { get; set; }

    /// <summary>包缓存目录。空表示数据目录下的 <c>Repos</c>。</summary>
    public string CachePath { get; set; } = "";

    /// <summary>全量恢复时用户指定的托管目录。空表示未做过全量恢复。</summary>
    public string ManagedDataDirectory { get; set; } = "";

    /// <summary>合法 JSON 里把某一项写成 <c>null</c> 是允许的，落到对象上不能留 null。</summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        Theme ??= "System";
        ManagedDataDirectory ??= "";
        AlcomTemplateChoice ??= "";
        Language ??= "";
        ButtonColor ??= "diamond";
        ProxyUrl ??= "";
        BackupFormat ??= "default";
        ProjectSorting ??= "lastModified";
        PluginsSorting ??= "name";
        BackupsSorting ??= "newest";
        BackupsFilter ??= "backup";
        LogsLevel ??= "Information";
        UnityHubAccessMethod ??= "read-config";
        CachePath ??= "";
        HiddenRepositories ??= [];
        DefaultUnityArguments ??= [];
        ProjectUnityArguments ??= [];
        FavoriteTemplates ??= [];
        FavoriteProjects ??= [];
        BackupRecords ??= [];
        BackupRecords.RemoveAll(x => x is null || string.IsNullOrWhiteSpace(x.FilePath));
        AutoBackupIntervalMinutes = Math.Clamp(AutoBackupIntervalMinutes, 15, 1440);
        AutoBackupRetention = Math.Clamp(AutoBackupRetention, 1, 100);
    }
}
