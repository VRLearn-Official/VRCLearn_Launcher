namespace VRCLearn.Plugin.Sdk;

public interface IGitUrlPackageInstaller
{
    Task<GitUrlInstallResult> InstallAsync(
        GitUrlInstallRequest request,
        IProgress<GitUrlInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record GitUrlInstallRequest(string Url, string? ProjectPath = null);

public enum GitUrlInstallStatus
{
    Installed,
    Cancelled,
    NoProject,
    ProjectNotUsable,
    ProjectRunning,
    InvalidUrl,
    InvalidAccelerator,
    DownloadFailed,
    InvalidArchive,
    PackageNotFound,
    InvalidPackage,
    TargetExists,
    ManifestConflict,
    Busy,
    IoError,
    Failed,
}

public sealed record GitUrlInstallResult(
    GitUrlInstallStatus Status,
    string? PackageName = null,
    string? InstallPath = null,
    string? Detail = null);

public enum GitUrlInstallPhase
{
    Resolving,
    Downloading,
    Extracting,
    Validating,
    Installing,
}

public sealed record GitUrlInstallProgress(
    GitUrlInstallPhase Phase,
    long Done,
    long? Total = null,
    string? PackageName = null);
