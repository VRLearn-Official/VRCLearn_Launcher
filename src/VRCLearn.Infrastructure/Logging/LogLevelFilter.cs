using Microsoft.Extensions.Logging;

namespace VRCLearn.Infrastructure.Logging;

public sealed class LogLevelFilter
{
    public LogLevel Minimum { get; set; } = LogLevel.Information;

    public void Apply(string? level) =>
        Minimum = Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;
}
