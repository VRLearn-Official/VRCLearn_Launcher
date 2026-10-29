using System.Text.Json;
using System.Text.Json.Serialization;
using VRCLearn.Core.Repositories;

namespace VRCLearn.Core.Vpm;

/// <summary>VCC 的 <c>settings.json</c>。</summary>
public sealed class LauncherSettings : IJsonOnDeserialized
{
    [JsonPropertyName("userRepos")]
    public List<UserRepoSetting> UserRepos { get; set; } = [];

    [JsonPropertyName("userProjects")]
    public List<string> UserProjects { get; set; } = [];

    /// <summary>备份放哪儿。空表示用数据目录下的 Backup/。</summary>
    [JsonPropertyName("projectBackupPath")]
    public string ProjectBackupPath { get; set; } = "";

    /// <summary>Unity Hub 可执行文件或所在目录。空表示自己去注册表和默认路径找。</summary>
    [JsonPropertyName("pathToUnityHub")]
    public string UnityHubPath { get; set; } = "";

    /// <summary>手动指定的 Unity 编辑器（可执行文件的绝对路径）。</summary>
    [JsonPropertyName("unityEditors")]
    public List<string> UnityEditors { get; set; } = [];

    /// <summary>Unity 大版本 → 编辑器路径，如 <c>"2022"</c>。</summary>
    [JsonPropertyName("preferredUnityEditors")]
    public Dictionary<string, string> PreferredUnityEditors { get; set; } = [];

    /// <summary>装/升级包的时候要不要接受预发布版本。</summary>
    [JsonPropertyName("showPrereleasePackages")]
    public bool ShowPrereleasePackages { get; set; }

    /// <summary>不认识但要原样留住的键。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Rest { get; set; } = [];

    /// <summary>用户包目录（绝对路径），每个里面放一个 <c>package.json</c>。</summary>
    [JsonPropertyName("userPackageFolders")]
    public List<string> UserPackageFolders { get; set; } = [];

    /// <summary>新建项目默认放到哪儿。空表示用 <see cref="EffectiveProjectPath"/> 算出来的那个。</summary>
    [JsonPropertyName("defaultProjectPath")]
    public string DefaultProjectPath { get; set; } = "";

    /// <summary>没设过默认项目路径时落在哪儿：<c>我的文档\VRCLearn\Projects</c>。</summary>
    public static string FallbackProjectPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "VRCLearn",
        "Projects");

    /// <summary>算出来的，不是设置。</summary>
    [JsonIgnore]
    public string EffectiveProjectPath =>
        string.IsNullOrWhiteSpace(DefaultProjectPath) ? FallbackProjectPath : DefaultProjectPath;

    public bool ContainsRepo(Uri url, string? id) =>
        UserRepos.Any(x => x.Url == url) || (id is not null && UserRepos.Any(x => x.Id == id));

    /// <summary>合法 JSON 里把某一项写成 <c>null</c> 是允许的，落到对象上不能留 null。</summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        UserRepos ??= [];
        UserProjects ??= [];
        UnityEditors ??= [];
        PreferredUnityEditors ??= [];
        UserPackageFolders ??= [];
        Rest ??= [];
        ProjectBackupPath ??= "";
        UnityHubPath ??= "";
        DefaultProjectPath ??= "";
    }
}
