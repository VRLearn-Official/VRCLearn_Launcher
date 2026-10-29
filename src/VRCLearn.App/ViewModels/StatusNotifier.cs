using Microsoft.Extensions.Logging;

namespace VRCLearn.App.ViewModels;

/// <summary>对话框内联的单行提示：写日志，到点自动清空。</summary>
public sealed class StatusNotifier(Action<string?> clear, ILogger logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    private readonly TransientAction _expiry = new(Lifetime, () => clear(null));

    public void Publish(string? value)
    {
        _expiry.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        logger.LogInformation("{Status}", value);
        _expiry.Run();
    }
}
