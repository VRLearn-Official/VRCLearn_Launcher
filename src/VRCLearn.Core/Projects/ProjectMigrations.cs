using System.Text;
using VRCLearn.Core.IO;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Versioning;
using VRCLearn.Core.Vpm;

namespace VRCLearn.Core.Projects;

public enum MigrateStatus
{
    Success,

    WrongProjectType,

    WrongUnityVersion,

    PackageNotFound,

    MissingDependencies,
}

public sealed record MigrateOutcome(
    MigrateStatus Status,
    PendingProjectChanges? Changes,
    string? Detail = null,
    IReadOnlyList<MissingDependency>? MissingDependencies = null);

public static class ProjectMigrations
{
    public static async Task<MigrateOutcome> MigrateVpmAsync(
        UnityProject project,
        PackageCatalog catalog,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        var projectType = project.DetectProjectType();

        if (projectType is not (ProjectType.LegacyWorlds or ProjectType.LegacyAvatars))
        {
            return new MigrateOutcome(MigrateStatus.WrongProjectType, null, projectType.ToString());
        }

        var sdkName = projectType == ProjectType.LegacyWorlds ? "com.vrchat.worlds" : "com.vrchat.avatars";

        if (catalog.Find(sdkName, VersionSelector.LatestFor(project.UnityVersion, includePrerelease))
            is not { } sdk)
        {
            return new MigrateOutcome(MigrateStatus.PackageNotFound, null, sdkName);
        }

        var outcome = await PackageOperations
            .AddAsync(project, catalog, [sdk], AddPackageOperation.InstallToDependencies, includePrerelease, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Status != AddPackageStatus.Success)
        {
            return new MigrateOutcome(
                outcome.Status == AddPackageStatus.MissingDependencies
                    ? MigrateStatus.MissingDependencies
                    : MigrateStatus.PackageNotFound,
                null,
                sdkName);
        }

        return new MigrateOutcome(MigrateStatus.Success, outcome.Changes);
    }

    public static async Task<MigrateOutcome> MigrateUnity2022Async(
        UnityProject project,
        PackageCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (project.UnityVersion.Major != 2019)
        {
            return new MigrateOutcome(MigrateStatus.WrongUnityVersion, null, project.UnityVersion.ToString());
        }

        project.RemoveUpmDependency("com.unity.xr.oculus.standalone");
        project.RemoveUpmDependency("com.unity.xr.openvr.standalone");

        var toInstall = new List<PackageInfo>();

        foreach (var name in MigratingPackages)
        {
            if (project.GetLocked(name) is null)
            {
                continue;
            }

            if (catalog.Find(
                name,
                VersionSelector.LatestFor(VrchatDefaults.Recommended2022Unity, includePrerelease: false))
                is not { } package)
            {
                return new MigrateOutcome(MigrateStatus.PackageNotFound, null, name);
            }

            toInstall.Add(package);
        }

        if (toInstall.Count == 0)
        {
            return new MigrateOutcome(MigrateStatus.Success, null);
        }

        var outcome = await PackageOperations
            .AddAsync(
                project,
                catalog,
                toInstall,
                AddPackageOperation.InstallToDependencies,
                allowPrerelease: false,
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Status != AddPackageStatus.Success)
        {
            return new MigrateOutcome(
                outcome.Status == AddPackageStatus.MissingDependencies
                    ? MigrateStatus.MissingDependencies
                    : MigrateStatus.PackageNotFound,
                null,
                MissingDependencies: outcome.MissingDependencies);
        }

        return new MigrateOutcome(MigrateStatus.Success, outcome.Changes);
    }

    public static async Task<bool> ApplyProjectSettingsAsync(
        IFileIo io,
        CancellationToken cancellationToken)
    {
        var bytes = await io.ReadAllBytesOrNullAsync(UnityProject.ProjectSettingsAsset, cancellationToken).ConfigureAwait(false);

        if (bytes is null)
        {
            return false;
        }

        var content = Encoding.UTF8.GetString(bytes);

        var changed = ReplaceSetting(
            ref content,
            "enableNativePlatformBackendsForNewInputSystem: ",
            "0",
            "1");

        changed |= ReplaceSetting(ref content, "disableOldInputManagerSupport: ", "1", "0");

        if (changed)
        {
            await io.WriteAllBytesAsync(UnityProject.ProjectSettingsAsset, Encoding.UTF8.GetBytes(content), cancellationToken)
                .ConfigureAwait(false);
        }

        return changed;
    }

    private static readonly string[] MigratingPackages =
    [
        "com.vrchat.base",
        "com.vrchat.avatars",
        "com.vrchat.worlds",
        "com.vrchat.core.vpm-resolver",
    ];

    /// <summary>只在第一次出现、且前面是空白处替换。</summary>
    private static bool ReplaceSetting(ref string content, string setting, string old, string value)
    {
        var position = content.IndexOf(setting, StringComparison.Ordinal);

        if (position < 0)
        {
            return false;
        }

        var start = position + setting.Length;

        if ((position > 0 && !char.IsWhiteSpace(content[position - 1]))
            || !content.AsSpan(start).StartsWith(old, StringComparison.Ordinal))
        {
            return false;
        }

        content = string.Concat(content.AsSpan(0, start), value, content.AsSpan(start + old.Length));
        return true;
    }
}
