using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;

namespace VRCLearn.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly Lock _gate = new();
    private readonly LogLevelFilter _filter;
    private readonly TextWriter _writer;
    private bool _disposed;
    private bool _failed;

    public FileLoggerProvider(LogLevelFilter filter, AppPaths paths)
    {
        _filter = filter;
        Path = System.IO.Path.Combine(paths.LogsDirectory, LogFiles.Name(DateTimeOffset.Now));

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);

            LogRetention.Apply(paths);

            _writer = new StreamWriter(Path, append: false) { AutoFlush = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _failed = true;
            _writer = TextWriter.Null;

            Debug.WriteLine($"日志文件打不开，本次只输出到调试器：{exception.Message}");
        }
    }

    internal FileLoggerProvider(LogLevelFilter filter, string path, TextWriter writer)
    {
        _filter = filter;
        Path = path;
        _writer = writer;
    }

    /// <summary>本次启动写的日志文件路径。</summary>
    public string Path { get; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _writer.Dispose();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                Debug.WriteLine($"关闭日志文件失败：{exception.Message}");
            }
        }
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed || _failed)
            {
                return;
            }

            try
            {
                _writer.WriteLine(line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                _failed = true;

                Debug.WriteLine($"写日志失败，后续日志丢弃：{exception.Message}");
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._filter.Minimum;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {category}: {formatter(state, exception)}";
            provider.Write(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }
    }
}
