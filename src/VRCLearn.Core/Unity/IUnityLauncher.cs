namespace VRCLearn.Core.Unity;

public enum UnityProjectStatus
{
    Closed,

    /// <summary>刚启起来，Unity 还没建立锁文件。</summary>
    Opening,

    Open,
}

public interface IUnityLauncher
{
    /// <summary>不等待 Unity 结束。</summary>
    void Launch(string executablePath, string projectDirectory, IReadOnlyList<string> arguments);

    UnityProjectStatus Status(string projectDirectory);

    bool IsRunning(string projectDirectory);
}

public static class UnityLaunchArguments
{
    /// <summary>项目路径在前，自定义参数跟在后面。</summary>
    public static IReadOnlyList<string> For(
        string projectDirectory,
        IReadOnlyList<string>? customArguments = null) =>
        ["-projectPath", projectDirectory, .. customArguments ?? []];
}
