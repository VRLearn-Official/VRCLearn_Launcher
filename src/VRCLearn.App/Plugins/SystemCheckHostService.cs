using VRCLearn.Infrastructure.Windows;
using Sdk = VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

public sealed class SystemCheckHostService(SystemCheckService systemCheck) : Sdk.ISystemCheckHost
{
    public async Task<IReadOnlyList<Sdk.SystemCheckResult>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var results = await systemCheck.RunAsync();

        return
        [
            .. results.Select(x => new Sdk.SystemCheckResult(Map(x.Kind), x.Passed, x.Detail)),
        ];
    }

    private static Sdk.SystemCheckKind Map(SystemCheckKind kind) => kind switch
    {
        SystemCheckKind.UserName => Sdk.SystemCheckKind.UserName,
        SystemCheckKind.DeviceName => Sdk.SystemCheckKind.DeviceName,
        SystemCheckKind.FileExtensions => Sdk.SystemCheckKind.FileExtensions,
        SystemCheckKind.UnityHub => Sdk.SystemCheckKind.UnityHub,
        SystemCheckKind.UnityEditor => Sdk.SystemCheckKind.UnityEditor,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
