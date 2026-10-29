using VRCLearn.Core.Configuration;
using VRCLearn.Core.IO;
using VRCLearn.Core.Vpm;

namespace VRCLearn.Core.Projects;

public enum AddProjectStatus
{
    Added,
    AlreadyAdded,

    NotAProject,
}

public enum RemoveProjectStatus
{
    Success,
    NotFound,

    /// <summary>磁盘上的目录没能删掉。</summary>
    Failed,
}

public enum RenameProjectStatus
{
    Success,
    NotFound,

    /// <summary>空名字，或者含文件系统不接受的字符。</summary>
    InvalidName,

    /// <summary>目标名字已经被别的目录占了。</summary>
    AlreadyExists,

    /// <summary>名字没问题，但磁盘上没能改（被占用、没权限、太长）。</summary>
    Failed,
}

/// <summary>列表只是启动器自己的记录，移除默认不碰磁盘上的项目。</summary>
public sealed class ProjectService
{
    private readonly ILauncherSettingsStore _settingsStore;
    private readonly IFileIoFactory _fileIo;

    public ProjectService(ILauncherSettingsStore settingsStore, IFileIoFactory fileIo)
    {
        _settingsStore = settingsStore;
        _fileIo = fileIo;
    }

    private LauncherSettings Settings => _settingsStore.Current;

    /// <summary>顺序就是添加顺序。</summary>
    public IReadOnlyList<string> List() => Settings.UserProjects;

    public static bool IsProject(string projectPath) =>
        HasProjectVersion(PathUtil.Normalize(projectPath));

    private static bool HasProjectVersion(string fullPath) =>
        File.Exists(Path.Combine(fullPath, ProjectVersionFile));

    public AddProjectStatus Add(string projectPath)
    {
        var fullPath = PathUtil.Normalize(projectPath);

        if (Settings.UserProjects.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            return AddProjectStatus.AlreadyAdded;
        }

        if (!HasProjectVersion(fullPath))
        {
            return AddProjectStatus.NotAProject;
        }

        Settings.UserProjects.Add(fullPath);
        _settingsStore.Save();

        return AddProjectStatus.Added;
    }

    public RemoveProjectStatus Remove(string projectPath)
    {
        var fullPath = PathUtil.Normalize(projectPath);
        var index = Settings.UserProjects.FindIndex(x => string.Equals(x, fullPath, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return RemoveProjectStatus.NotFound;
        }

        Settings.UserProjects.RemoveAt(index);
        _settingsStore.Save();

        return RemoveProjectStatus.Success;
    }

    /// <summary>删掉项目目录，再把它从列表里去掉。目录没删掉时列表保持不动。</summary>
    public RemoveProjectStatus RemoveFromDisk(string projectPath, IProgress<FileProgress>? progress = null)
    {
        var fullPath = PathUtil.Normalize(projectPath);

        if (Path.GetDirectoryName(fullPath) is not { } parent)
        {
            return RemoveProjectStatus.Failed;
        }

        try
        {
            _fileIo.For(parent).DeleteDirectoryTree(Path.GetFileName(fullPath), progress);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RemoveProjectStatus.Failed;
        }

        return Remove(fullPath);
    }

    /// <summary>改项目目录的名字，列表里记的路径跟着改。</summary>
    public RenameProjectStatus Rename(string projectPath, string newName)
    {
        var fullPath = PathUtil.Normalize(projectPath);
        var index = Settings.UserProjects.FindIndex(x => string.Equals(x, fullPath, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return RenameProjectStatus.NotFound;
        }

        if (!IsValidName(newName) || Path.GetDirectoryName(fullPath) is not { Length: > 0 } parentPath)
        {
            return RenameProjectStatus.InvalidName;
        }

        var parent = _fileIo.For(parentPath);

        if (parent.DirectoryExists(newName))
        {
            return RenameProjectStatus.AlreadyExists;
        }

        try
        {
            parent.MoveDirectory(Path.GetFileName(fullPath), newName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return RenameProjectStatus.Failed;
        }

        Settings.UserProjects[index] = Path.Combine(parentPath, newName);
        _settingsStore.Save();

        return RenameProjectStatus.Success;
    }

    public static bool IsValidName(string name) => PathUtil.IsSafePathSegment(name);

    public Task<UnityProject> OpenAsync(string projectPath, CancellationToken cancellationToken) =>
        UnityProject.LoadAsync(_fileIo.For(PathUtil.Normalize(projectPath)), cancellationToken);

    private static string ProjectVersionFile =>
        UnityProject.ProjectVersionPath.Replace('/', Path.DirectorySeparatorChar);
}
