using VRCLearn.Core.IO;

namespace VRCLearn.Infrastructure.IO;

public sealed class PhysicalFileIo : IFileIo
{
    private readonly Func<string> _root;

    public PhysicalFileIo(string rootDirectory)
        : this(() => rootDirectory)
    {
    }

    public PhysicalFileIo(Func<string> rootProvider) => _root = rootProvider;

    public string RootDirectory => _root();

    public async Task<byte[]?> ReadAllBytesOrNullAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = Resolve(relativePath);
        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task WriteAllBytesAsync(
        string relativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var path = Resolve(relativePath);
        EnsureParentDirectory(path);

        var temporary = $"{path}.temp.{Guid.NewGuid():N}";

        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            FileCleanup.TryDelete(temporary);
            throw;
        }
    }

    public Task<Stream?> OpenReadOrNullAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = Resolve(relativePath);

        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task<Stream> CreateAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = Resolve(relativePath);
        EnsureParentDirectory(path);

        return Task.FromResult<Stream>(File.Create(path));
    }

    private static void EnsureParentDirectory(string path)
    {
        if (Path.GetDirectoryName(path) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }
    }

    public IReadOnlyList<FileEntry> ListEntries(string relativePath)
    {
        var path = Resolve(relativePath);

        if (!Directory.Exists(path))
        {
            return [];
        }

        return
        [
            .. new DirectoryInfo(path)
                .EnumerateFileSystemInfos()
                .Select(x => new FileEntry(
                    x.Name,
                    x is DirectoryInfo,
                    (x.Attributes & FileAttributes.ReparsePoint) != 0))
        ];
    }

    public bool FileExists(string relativePath) => File.Exists(Resolve(relativePath));

    public bool DirectoryExists(string relativePath) => Directory.Exists(Resolve(relativePath));

    public void CreateDirectory(string relativePath) => Directory.CreateDirectory(Resolve(relativePath));

    public void MoveDirectory(string sourceRelativePath, string destinationRelativePath) =>
        Directory.Move(Resolve(sourceRelativePath), Resolve(destinationRelativePath));

    public void DeleteDirectory(string relativePath)
    {
        var path = Resolve(relativePath);

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public bool DeleteFile(string relativePath)
    {
        var path = Resolve(relativePath);

        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private string Resolve(string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root()));
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsInside(root, full))
        {
            throw new ArgumentException($"路径跑到根目录外面去了：{relativePath}", nameof(relativePath));
        }

        return full;
    }

    private static bool IsInside(string root, string path)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
