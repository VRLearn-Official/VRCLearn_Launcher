namespace VRCLearn.Infrastructure.Logging;

/// 文件名须保证按字典序排列即按时间排序。
public static class LogFiles
{
    public const string Pattern = "vcl-*.log";

    public static string Name(DateTimeOffset startedAt) =>
        $"vcl-{startedAt:yyyyMMdd-HHmmss-fff}.log";
}
