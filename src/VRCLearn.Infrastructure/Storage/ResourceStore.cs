using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Resources;

namespace VRCLearn.Infrastructure.Storage;

public sealed class ResourceStore(
    AppPaths paths,
    GuiConfigStore configs,
    ILogger<ResourceStore> logger) : IResourceStore
{
    private readonly Lock _gate = new();
    private ResourceCollection? _current;

    public ResourceCollection Current
    {
        get
        {
            lock (_gate)
            {
                return _current ??= JsonFileStore.Load<ResourceCollection>(
                    paths.ResourcesFile,
                    message => logger.LogWarning("{Message}", message),
                    message => logger.LogWarning("{Message}", message));
            }
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            _current = null;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            JsonFileStore.Save(
                paths.ResourcesFile,
                Current,
                message => logger.LogWarning("{Message}", message));
        }
    }

    public string CoverPath(ResourceDefinition resource) =>
        string.IsNullOrWhiteSpace(resource.CoverPath) ? "" : ResolveCoverPath(resource.CoverPath);

    private string ResolveCoverPath(string coverPath)
    {
        try
        {
            var path = Path.IsPathFullyQualified(coverPath)
                ? Path.GetFullPath(coverPath)
                : Path.GetFullPath(Path.Combine(
                    paths.ResourcesDirectory,
                    coverPath.Replace('/', Path.DirectorySeparatorChar)));

            return IsAllowed(path) ? path : "";
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogWarning(exception, "封面路径不合法：{Path}", coverPath);
            return "";
        }
    }

    private bool IsAllowed(string path)
    {
        if (IsUnder(path, paths.ResourcesDirectory))
        {
            return true;
        }

        var managed = configs.Current.ManagedDataDirectory;

        return managed.Length > 0
            && Path.IsPathFullyQualified(managed)
            && IsUnder(path, managed);
    }

    private static bool IsUnder(string path, string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        return path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public void SaveCover(ResourceDefinition resource, ReadOnlyMemory<byte> png)
    {
        var relative = $"covers/{resource.Id}.png";
        var path = ResolveCoverPath(relative);

        if (path.Length == 0)
        {
            logger.LogWarning("资源 Id 不能用作封面文件名：{Id}", resource.Id);
            return;
        }

        Directory.CreateDirectory(paths.ResourceCoversDirectory);
        File.WriteAllBytes(path, png.ToArray());

        resource.CoverPath = relative;
    }

    public void DeleteCover(ResourceDefinition resource)
    {
        var path = CoverPath(resource);

        if (path.Length == 0)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "删除资源封面失败：{Path}", path);
        }
    }
}
