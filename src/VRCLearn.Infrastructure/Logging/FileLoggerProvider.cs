using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;

namespace VRCLearn.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly Lock _gate = new();
    private readonly LogLevelFilter _filter;
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileLoggerProvider(LogLevelFilter filter, AppPaths paths)
    {
        _filter = filter;

        Directory.CreateDirectory(paths.LogsDirectory);

        LogRetention.Apply(paths);

        Path = System.IO.Path.Combine(paths.LogsDirectory, LogFiles.Name(DateTimeOffset.Now));
        _writer = new StreamWriter(Path, append: false) { AutoFlush = true };
    }

    /// 本次启动写的日志文件路径。
    public string Path { get; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine(line);
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
