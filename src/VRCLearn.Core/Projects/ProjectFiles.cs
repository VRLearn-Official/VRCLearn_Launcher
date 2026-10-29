using VRCLearn.Core.IO;

namespace VRCLearn.Core.Projects;

public sealed record ProjectFileFilter(bool ExcludeLibrary)
{
    public static ProjectFileFilter ForBackup() => new(ExcludeLibrary: true);
}

/// <summary>目录一并列出来，空目录也得留住。</summary>
public sealed record ProjectContent(IReadOnlyList<string> Directories, IReadOnlyList<string> Files);

public sealed record FileProgress(int Total, int Done);

public static class ProjectFiles
{
    private const string LibraryPrefix = "library";

    private const string LastSceneManagerSetup = "LastSceneManagerSetup.txt";
    private const string GitDirectory = ".git";

    private static readonly string[] RootNoise = ["logs", "obj", "temp"];

    public static ProjectContent Collect(IFileIo io, ProjectFileFilter filter)
    {
        var directories = new List<string>();
        var files = new List<string>();

        Walk(io, "", filter, directories, files);

        return new ProjectContent(directories, files);
    }

    public static ProjectContent Collect(UnityProject project, ProjectFileFilter filter) =>
        Collect(project.Io, filter);

    private static void Walk(
        IFileIo io,
        string relative,
        ProjectFileFilter filter,
        List<string> directories,
        List<string> files)
    {
        foreach (var entry in io.ListEntries(relative))
        {
            if (entry.IsSymbolicLink)
            {
                continue;
            }

            var path = relative.Length == 0 ? entry.Name : $"{relative}/{entry.Name}";

            if (entry.IsDirectory)
            {
                if (!SkipDirectory(relative, entry.Name, filter))
                {
                    directories.Add(path);
                    Walk(io, path, filter, directories, files);
                }

                continue;
            }

            if (!SkipFile(relative, entry.Name, filter))
            {
                files.Add(path);
            }
        }
    }

    private static bool SkipDirectory(string parent, string name, ProjectFileFilter filter)
    {
        var lowered = name.ToLowerInvariant();

        if (parent.Length == 0 && RootNoise.Contains(lowered))
        {
            return true;
        }

        if (filter.ExcludeLibrary && IsInsideLibrary(parent))
        {
            return true;
        }

        return lowered == GitDirectory;
    }

    private static bool SkipFile(string parent, string name, ProjectFileFilter filter)
    {
        if (!filter.ExcludeLibrary || !IsInsideLibrary(parent))
        {
            return false;
        }

        return !name.Equals(LastSceneManagerSetup, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideLibrary(string relative) =>
        relative.Length >= LibraryPrefix.Length
        && relative.AsSpan(0, LibraryPrefix.Length).Equals(LibraryPrefix, StringComparison.OrdinalIgnoreCase);

}
