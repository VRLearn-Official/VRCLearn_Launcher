namespace VRCLearn.Core.IO;

/// <summary>相对路径以根目录为基准，分隔符统一用 '/'。</summary>
public interface IFileIo
{
    string RootDirectory { get; }

    /// <summary>文件不存在时返回 null。</summary>
    Task<byte[]?> ReadAllBytesOrNullAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>原子写入，父目录自动创建。</summary>
    Task WriteAllBytesAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>文件不存在时返回 null。用完由调用方关闭。</summary>
    Task<Stream?> OpenReadOrNullAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>覆盖写，父目录自动创建。用完由调用方关闭。</summary>
    Task<Stream> CreateAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>目录不存在时返回空列表。</summary>
    IReadOnlyList<FileEntry> ListEntries(string relativePath);

    bool FileExists(string relativePath);

    bool DirectoryExists(string relativePath);

    void CreateDirectory(string relativePath);

    /// <summary>源目录不存在时抛 <see cref="DirectoryNotFoundException"/>。</summary>
    void MoveDirectory(string sourceRelativePath, string destinationRelativePath);

    /// <summary>目录不存在时不报错。</summary>
    void DeleteDirectory(string relativePath);

    /// <summary>删掉了返回 true，本来就不存在返回 false。</summary>
    bool DeleteFile(string relativePath);
}
