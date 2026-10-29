namespace VRCLearn.Core.Configuration;

public sealed class InsufficientDiskSpaceException(long requiredBytes, long availableBytes)
    : IOException($"磁盘空间不足：需要 {requiredBytes} 字节，可用 {availableBytes} 字节")
{
    public long RequiredBytes { get; } = requiredBytes;

    public long AvailableBytes { get; } = availableBytes;
}

public static class DiskSpace
{
    private const long MinimumMargin = 200L * 1024 * 1024;

    public static long? AvailableBytes(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);

            while (!Directory.Exists(full))
            {
                var parent = Path.GetDirectoryName(full);

                if (parent is null || parent == full)
                {
                    break;
                }

                full = parent;
            }

            var root = Path.GetPathRoot(full);

            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Ensure(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
        {
            return;
        }

        if (AvailableBytes(path) is not { } available)
        {
            return;
        }

        var margin = Math.Max(MinimumMargin, requiredBytes / 20);

        if (available <= margin || requiredBytes > available - margin)
        {
            throw new InsufficientDiskSpaceException(requiredBytes, available);
        }
    }
}
