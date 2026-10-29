using System.Text.Json;
using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.IO;

namespace VRCLearn.Infrastructure.Storage;

internal static class JsonFileStore
{
    public static T Load<T>(
        string path,
        Action<string>? onCorrupt = null,
        Action<string>? onUnreadable = null)
        where T : class, new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Indented) ?? new T();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new T();
        }
        catch (JsonException exception)
        {
            onCorrupt?.Invoke(Preserve(path, exception));
            return new T();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            onUnreadable?.Invoke($"{path} 读不出来（{exception.Message}），本次不会写回");
            return new T();
        }
    }

    /// <summary>只读不处理：解析不了就当没有，不挪文件、不写回。</summary>
    public static T? Peek<T>(string path)
        where T : class, new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Indented);
        }
        catch (Exception exception) when (exception is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save<T>(string path, T value, Action<string>? onError = null)
    {
        try
        {
            AtomicTextFile.Write(path, JsonSerializer.Serialize(value, JsonDefaults.Indented));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            onError?.Invoke($"{path} 存不下去（{exception.Message}），这次的改动没落盘");
        }
    }

    private static string Preserve(string path, JsonException cause)
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var target = $"{path}.broken-{stamp}";

        for (var sequence = 1; File.Exists(target); sequence++)
        {
            target = $"{path}.broken-{stamp}-{sequence}";
        }

        try
        {
            File.Move(path, target);
            return $"{path} 解析失败（{cause.Message}），已挪到 {target}，本次按默认设置启动";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"{path} 解析失败（{cause.Message}）：{exception.Message}";
        }
    }
}
