using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.Infrastructure.Logging;

public static class LogRetention
{
    /// 只在 <see cref="FileLoggerProvider"/> 构造时调用。
    public static void Apply(AppPaths paths) => Trim(paths.LogsDirectory, ConfiguredCount(paths));

    /// keep 含本次要写的那份。
    public static void Trim(string directory, int keep)
    {
        if (keep <= 0 || !Directory.Exists(directory))
        {
            return;
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(directory, LogFiles.Pattern);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        Array.Sort(files, StringComparer.Ordinal);

        var stale = files.Length - keep + 1;

        for (var i = 0; i < stale; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static int ConfiguredCount(AppPaths paths) =>
        JsonFileStore.Peek<GuiConfig>(paths.GuiConfigFile)?.LogsRetentionCount
        ?? new GuiConfig().LogsRetentionCount;
}
