namespace VRCLearn.Core.Unity;

public interface IUnityHubLauncher
{
    /// <summary>没装返回 null。</summary>
    string? ExecutablePath { get; }

    bool IsRunning { get; }

    /// <summary>Hub 没在跑就启起来，并等界面显示出来。</summary>
    Task<UnityHubStartResult> EnsureRunningAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>让 Hub 用 <c>--headless editors -i</c> 报一遍。</summary>
    Task<IReadOnlyList<UnityEditor>> ListEditorsAsync(CancellationToken cancellationToken);
}

public enum UnityHubStartResult
{
    AlreadyRunning,

    Started,

    NotFound,

    Failed,

    Timeout,
}

public static class UnityHubStartResultExtensions
{
    public static bool IsUsable(this UnityHubStartResult result) =>
        result is UnityHubStartResult.AlreadyRunning or UnityHubStartResult.Started;
}
