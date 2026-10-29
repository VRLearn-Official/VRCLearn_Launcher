using Microsoft.Extensions.Logging;

namespace VRCLearn.Infrastructure.Logging;

public sealed record LogLine(LogLevel Level, string Text)
{
    public static LogLine Parse(string line) =>
        new(LevelOf(line) ?? LogLevel.Information, line);

    /// <summary>找不到 <c>[级别]</c>（异常的续行）时返回 null。</summary>
    internal static LogLevel? LevelOf(string line)
    {
        var open = line.IndexOf('[', StringComparison.Ordinal);
        var close = open < 0 ? -1 : line.IndexOf(']', open + 1);

        if (close < 0)
        {
            return null;
        }

        var name = line[(open + 1)..close];

        return Enum.TryParse<LogLevel>(name, ignoreCase: true, out var level) ? level : null;
    }
}

public static class LogFile
{
    private const long DefaultMaxBytes = 512 * 1024;

    public static IReadOnlyList<LogLine> ReadTail(string path, long maxBytes = DefaultMaxBytes)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var skip = start > 0 && lines.Length > 0 ? 1 : 0;

        var result = new List<LogLine>(lines.Length);
        var level = LogLevel.Information;

        foreach (var raw in lines.Skip(skip))
        {
            var line = raw.TrimEnd('\r');

            level = LogLine.LevelOf(line) ?? level;

            result.Add(new LogLine(level, line));
        }

        return result;
    }
}
