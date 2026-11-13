using VRCLearn.Core.IO;
using System.Runtime.ExceptionServices;
using VRCLearn.Core.Packages;

namespace VRCLearn.Core.Projects;

public static class ProjectChangesApplier
{
    private const string PackagesDirectory = UnityProject.PackagesDirectory;

    private const string TempBase = "Temp/vrc-get";

    /// <summary>返回回滚后仍没删掉的资产路径；安装本身算成功。</summary>
    public static async Task<IReadOnlyList<string>> ApplyAsync(
        UnityProject project,
        IPackageInstaller installer,
        PendingProjectChanges changes,
        CancellationToken cancellationToken,
        IProgress<FileProgress>? progress = null)
    {
        var io = project.Io;
        var installs = new List<PackageInfo>();
        var removeNames = new List<string>();
        var uninstallNames = new List<string>();

        foreach (var (name, change) in changes.PackageChanges)
        {
            switch (change)
            {
                case Install { Package: { } package }:
                    installs.Add(package);
                    uninstallNames.Add(name);
                    break;

                case Install:
                    break;

                case Remove:
                    removeNames.Add(name);
                    uninstallNames.Add(name);
                    break;
            }
        }

        foreach (var conflict in changes.Conflicts.Values)
        {
            uninstallNames.AddRange(conflict.UnlockedNames);
        }

        var transactionDirectory = $"{TempBase}/{Guid.NewGuid():N}";
        var context = new InstallContext($"{transactionDirectory}/remove", $"{transactionDirectory}/install");

        io.CreateDirectory(context.RemoveDirectory);
        io.CreateDirectory(context.InstallDirectory);

        try
        {
            await ExtractAsync(io, installer, context.InstallDirectory, installs, cancellationToken, progress)
                .ConfigureAwait(false);

            context.MoveUninstallPackages(io, uninstallNames);
            context.MoveInstallPackages(io, installs);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateManifest(project, changes, removeNames);

            await project.SaveAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RollbackAsync(project, context, transactionDirectory, exception).ConfigureAwait(false);

            throw;
        }

        try
        {
            io.DeleteDirectory(TempBase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return RemoveLegacyAssets(io, changes.RemoveLegacyFiles, changes.RemoveLegacyFolders);
    }

    private static void UpdateManifest(
        UnityProject project,
        PendingProjectChanges changes,
        IReadOnlyList<string> removeNames)
    {
        foreach (var (name, change) in changes.PackageChanges)
        {
            if (change is not Install install)
            {
                continue;
            }

            if (install is { Package: { } package, AddToLocked: true })
            {
                project.Manifest.AddLocked(package.Name, package.Version, package.VpmDependencies);
            }

            if (install.ToDependencies is { } version)
            {
                project.Manifest.AddDependency(name, version);
            }
        }

        project.Manifest.RemovePackages(removeNames);
    }

    private static async Task RollbackAsync(
        UnityProject project,
        InstallContext context,
        string transactionDirectory,
        Exception originalError)
    {
        try
        {
            context.Rollback(project.Io);
        }
        catch (Exception rollbackError)
        {
            throw new AggregateException(
                $"回滚失败，残留目录在 {transactionDirectory}"
                    + $"（已装未还原：{string.Join(", ", context.Installed)}；"
                    + $"已移除未还原：{string.Join(", ", context.Removed)}）",
                originalError,
                rollbackError);
        }

        try
        {
            await project.ReloadManifestsAsync(CancellationToken.None).ConfigureAwait(false);
            project.Io.DeleteDirectory(TempBase);
        }
        catch
        {
        }
    }

    private static async Task ExtractAsync(
        IFileIo io,
        IPackageInstaller installer,
        string installDirectory,
        IReadOnlyList<PackageInfo> packages,
        CancellationToken cancellationToken,
        IProgress<FileProgress>? progress)
    {
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? failure = null;

        var remaining = new List<PackageInfo>(packages);
        var total = packages.Count;

        if (total > 0)
        {
            progress?.Report(new FileProgress(total, 0, Label(remaining[0])));
        }

        await Task.WhenAll(packages.Select(async package =>
        {
            try
            {
                await installer
                    .InstallPackageAsync(io, package, $"{installDirectory}/{package.Name}", abort.Token)
                    .ConfigureAwait(false);

                lock (remaining)
                {
                    remaining.Remove(package);

                    progress?.Report(new FileProgress(
                        total,
                        total - remaining.Count,
                        remaining.Count == 0 ? null : Label(remaining[0])));
                }
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref failure, exception, null);
                abort.Cancel();
            }
        })).ConfigureAwait(false);

        if (failure is null)
        {
            return;
        }

        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string Label(PackageInfo package) =>
        package.Manifest.DisplayName is { Length: > 0 } displayName ? displayName : package.Name;

    private static IReadOnlyList<string> RemoveLegacyAssets(
        IFileIo io,
        IReadOnlyList<LegacyAsset> files,
        IReadOnlyList<LegacyAsset> folders)
    {
        var failed = new HashSet<string>();

        foreach (var asset in files)
        {
            Try(failed, asset.Path, () => io.DeleteFile(asset.Path));
            Try(failed, asset.Path, () => io.DeleteFile(asset.Path + ".meta"));
        }

        foreach (var asset in folders)
        {
            Try(failed, asset.Path, () => io.DeleteDirectory(asset.Path));

            Try(failed, asset.Path, () => io.DeleteFile(asset.Path + ".meta"));
        }

        return [.. failed];
    }

    private static void Try(HashSet<string> failed, string path, Action remove)
    {
        try
        {
            remove();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failed.Add(path);
        }
    }

    private sealed class InstallContext(string removeDirectory, string installDirectory)
    {
        private readonly HashSet<string> _removed = [];
        private readonly HashSet<string> _installed = [];

        public string RemoveDirectory { get; } = removeDirectory;

        public string InstallDirectory { get; } = installDirectory;

        public IReadOnlyCollection<string> Removed => _removed;

        public IReadOnlyCollection<string> Installed => _installed;

        public void MoveUninstallPackages(IFileIo io, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var from = $"{PackagesDirectory}/{name}";

                if (_removed.Contains(name) || !io.DirectoryExists(from))
                {
                    continue;
                }

                io.MoveDirectory(from, $"{RemoveDirectory}/{name}");
                _removed.Add(name);
            }
        }

        public void MoveInstallPackages(IFileIo io, IEnumerable<PackageInfo> packages)
        {
            foreach (var package in packages)
            {
                io.MoveDirectory(
                    $"{InstallDirectory}/{package.Name}",
                    $"{PackagesDirectory}/{package.Name}");

                _installed.Add(package.Name);
            }
        }

        public void Rollback(IFileIo io)
        {
            foreach (var name in _installed)
            {
                io.MoveDirectory($"{PackagesDirectory}/{name}", $"{InstallDirectory}/{name}");
            }

            foreach (var name in _removed)
            {
                io.MoveDirectory($"{RemoveDirectory}/{name}", $"{PackagesDirectory}/{name}");
            }

            _installed.Clear();
            _removed.Clear();
        }
    }
}
