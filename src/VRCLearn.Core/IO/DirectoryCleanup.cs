namespace VRCLearn.Core.IO;

/// <summary>删目录失败不抛，用于清理临时目录。</summary>
public static class DirectoryCleanup
{
    public static bool TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
