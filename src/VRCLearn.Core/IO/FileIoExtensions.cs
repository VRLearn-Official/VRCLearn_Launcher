using VRCLearn.Core.Projects;

namespace VRCLearn.Core.IO;

public static class FileIoExtensions
{
    /// <summary>目录不存在时返回空列表。</summary>
    public static IReadOnlyList<string> ListDirectories(this IFileIo io, string relativePath) =>
        [.. io.ListEntries(relativePath).Where(x => x.IsDirectory).Select(x => x.Name)];

    /// <summary>
    /// 递归删目录，逐个文件上报进度。目录不存在时不报错。
    /// 不传进度就直接交给底层删；传了就先数一遍文件再逐个删，最后收掉空目录和符号链接。
    /// </summary>
    public static void DeleteDirectoryTree(this IFileIo io, string relativePath, IProgress<FileProgress>? progress)
    {
        if (progress is null || !io.DirectoryExists(relativePath))
        {
            io.DeleteDirectory(relativePath);
            return;
        }

        var files = io.ListFiles(relativePath, recursive: true);
        var done = 0;

        foreach (var file in files)
        {
            var path = $"{relativePath}/{file}";

            io.DeleteFile(path);

            progress.Report(new FileProgress(files.Count, ++done));
        }

        io.DeleteDirectory(relativePath);
    }

    /// <summary>不跟符号链接；目录不存在时返回空列表。</summary>
    public static IReadOnlyList<string> ListFiles(this IFileIo io, string relativePath, bool recursive)
    {
        var files = new List<string>();
        Collect(io, relativePath, "", recursive, files);
        return files;
    }

    private static void Collect(
        IFileIo io,
        string root,
        string relative,
        bool recursive,
        List<string> files)
    {
        var prefix = relative.Length == 0 ? "" : relative + "/";

        foreach (var entry in io.ListEntries(Join(root, relative)))
        {
            if (entry.IsSymbolicLink)
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                if (recursive)
                {
                    Collect(io, root, prefix + entry.Name, recursive, files);
                }

                continue;
            }

            files.Add(prefix + entry.Name);
        }
    }

    private static string Join(string left, string right) =>
        right.Length == 0 ? left : $"{left}/{right}";
}
