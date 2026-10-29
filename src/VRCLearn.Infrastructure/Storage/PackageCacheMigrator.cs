using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.IO;

namespace VRCLearn.Infrastructure.Storage;

/// <summary>把包压缩包搬到新缓存目录，同名文件直接覆盖。仓库清单的 <c>*.json</c> 留在原地，缓存根目录本身也不删。</summary>
internal static class PackageCacheMigrator
{
    public static PackageCacheMigrationResult Migrate(
        AppPaths paths,
        string source,
        string destination,
        IProgress<FileProgress>? progress = null)
    {
        string target;

        try
        {
            target = Path.GetFullPath(destination);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new PackageCacheMigrationResult(PackageCacheMigrationStatus.Failed, Error: exception.Message);
        }

        if (!PackageCachePath.IsSafeDestination(paths, target))
        {
            return new PackageCacheMigrationResult(PackageCacheMigrationStatus.UnsafeDestination);
        }

        if (PathUtil.SamePath(source, target)
            || PathUtil.IsUnder(source, target)
            || PathUtil.IsUnder(target, source))
        {
            return new PackageCacheMigrationResult(PackageCacheMigrationStatus.OverlappingPaths);
        }

        var packages = Directory.Exists(source) ? Directory.GetDirectories(source) : [];

        if (packages.Length == 0)
        {
            return new PackageCacheMigrationResult(PackageCacheMigrationStatus.NothingToMove);
        }

        var bytes = 0L;
        var total = CountFiles(packages);
        var moved = 0;

        void Report() => progress?.Report(new FileProgress(total, ++moved));

        try
        {
            Directory.CreateDirectory(target);

            foreach (var package in packages)
            {
                bytes += MovePackage(
                    package,
                    Path.Combine(target, Path.GetFileName(package)),
                    progress is null ? null : Report);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PackageCacheMigrationResult(PackageCacheMigrationStatus.Failed, bytes, exception.Message);
        }

        foreach (var package in packages)
        {
            RemoveIfEmpty(package);
        }

        return new PackageCacheMigrationResult(PackageCacheMigrationStatus.Migrated, bytes);
    }

    private static int CountFiles(IEnumerable<string> packages) =>
        packages.Sum(x => Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories).Count());

    private static long MovePackage(string source, string target, Action? onMoved)
    {
        var bytes = 0L;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(source, file));

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            bytes += new FileInfo(file).Length;
            FileMover.Move(file, to);

            onMoved?.Invoke();
        }

        return bytes;
    }

    private static void RemoveIfEmpty(string directory)
    {
        try
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return;
            }

            Directory.Delete(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
