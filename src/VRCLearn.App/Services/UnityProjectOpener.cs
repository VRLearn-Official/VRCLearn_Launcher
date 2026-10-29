using Microsoft.Extensions.Logging;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.Services;

public sealed class UnityProjectOpener(
    IUnityEditorLocator editors,
    IUnityHubLauncher hub,
    IUnityLauncher launcher,
    IUnityWindowActivator windows,
    ILauncherSettingsStore settings,
    GuiConfigStore configStore,
    AutoBackupService autoBackups,
    ILogger<UnityProjectOpener> logger)
{
    private static readonly TimeSpan HubTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(10);

    public UnityProjectStatus Status(string projectPath) => launcher.Status(projectPath);

    public bool IsBusy(string projectPath) =>
        hub.IsRunning || launcher.Status(projectPath) != UnityProjectStatus.Closed;

    public IReadOnlyList<UnityEditor> AllEditors() => editors.FindEditors();

    public IReadOnlyList<UnityEditor> EditorsFor(UnityVersion version) =>
        [.. editors.FindEditors().Where(x => x.MatchesProject(version))];

    public UnityEditor? FindEditor(UnityVersion version) =>
        UnityEditorList.Pick(EditorsFor(version), settings.Current.PreferredUnityEditors, version.Major);

    /// <summary>优先用项目自己的参数，否则用全局设置。</summary>
    public IReadOnlyList<string> ArgumentsFor(string projectPath)
    {
        var config = configStore.Current;

        return config.ProjectUnityArguments.TryGetValue(projectPath, out var own) && own.Count > 0
            ? own
            : config.DefaultUnityArguments;
    }

    public BringUnityToFrontResult BringToFront(string projectPath) => windows.BringToFront(projectPath);

    public async Task<UnityOpenOutcome> OpenAsync(
        string projectPath,
        UnityEditor? editor,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var opening = OpenCoreAsync(projectPath, editor, arguments, cancellationToken);

        if (await Task.WhenAny(opening, Task.Delay(OverallTimeout, cancellationToken)) == opening)
        {
            return await opening;
        }

        logger.LogWarning("启动 Unity 超时：{Project}", projectPath);

        return new UnityOpenOutcome(UnityOpenResult.Timeout, UnityHubStartResult.Timeout, null);
    }

    private async Task<UnityOpenOutcome> OpenCoreAsync(
        string projectPath,
        UnityEditor? editor,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (launcher.Status(projectPath) != UnityProjectStatus.Closed)
        {
            logger.LogInformation("Unity 已经在运行：{Project}", projectPath);

            return new UnityOpenOutcome(UnityOpenResult.AlreadyOpen, UnityHubStartResult.AlreadyRunning, null);
        }

        if (editor is null)
        {
            logger.LogWarning("没有可用的 Unity 编辑器：{Project}", projectPath);

            return new UnityOpenOutcome(UnityOpenResult.NoEditor, UnityHubStartResult.AlreadyRunning, null);
        }

        var hubResult = await hub.EnsureRunningAsync(HubTimeout, cancellationToken);

        if (hubResult is UnityHubStartResult.NotFound or UnityHubStartResult.Failed or UnityHubStartResult.Timeout)
        {
            logger.LogWarning("Unity Hub 不可用（{Result}）：{Project}", hubResult, projectPath);
        }

        try
        {
            launcher.Launch(editor.ExecutablePath, projectPath, UnityLaunchArguments.For(projectPath, arguments));

            logger.LogInformation("启动 Unity：{Editor} 打开 {Project}", editor.ExecutablePath, projectPath);

            autoBackups.ProjectStarted();

            return new UnityOpenOutcome(UnityOpenResult.Launched, hubResult, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "启动 Unity 失败：{Project}", projectPath);

            return new UnityOpenOutcome(UnityOpenResult.Failed, hubResult, exception.Message);
        }
    }
}

public enum UnityOpenResult
{
    AlreadyOpen,

    NoEditor,

    Launched,

    Timeout,

    Failed,
}

/// <param name="Detail">失败原因。</param>
public sealed record UnityOpenOutcome(UnityOpenResult Result, UnityHubStartResult Hub, string? Detail);
