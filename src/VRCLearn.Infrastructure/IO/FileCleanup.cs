namespace VRCLearn.Infrastructure.IO;

/// <summary>删文件失败也不抛，用于清掉下载中断留下的半截文件。</summary>
internal static class FileCleanup
{
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
