using VRCLearn.Core.Plugins;
using VRCLearn.Infrastructure.Plugins;

namespace VRCLearn.App.Plugins;

public enum PluginStatus
{
    Disabled,

    Ready,

    Loaded,

    MissingDependency,

    Faulted,

    PendingRestart,
}

public sealed record PluginSnapshot(
    InstalledPlugin Installed,
    bool Enabled,
    bool IsLoaded,
    PluginStatus Status,
    IReadOnlyList<string> Missing,
    bool PendingInstall,
    bool PendingRemoval,
    string? Error)
{
    public PluginManifest? Manifest => Installed.Manifest;

    public bool CanOpen =>
        Enabled
        && Manifest is not null
        && Status is PluginStatus.Ready or PluginStatus.Loaded;
}

public sealed record PluginActionFailure(
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Dependents);
