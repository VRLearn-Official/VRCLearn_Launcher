namespace VRCLearn.Plugin.Sdk;

public interface ISystemCheckHost
{
    Task<IReadOnlyList<SystemCheckResult>> RunAsync(CancellationToken cancellationToken = default);
}

public enum SystemCheckKind
{
    UserName,
    DeviceName,
    FileExtensions,
    UnityHub,
    UnityEditor,
}

public sealed record SystemCheckResult(SystemCheckKind Kind, bool Passed, string Detail);
