using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Services;

/// <summary>窗口右下角浮出来的单行通知。</summary>
public sealed class ToastService(ILogger<ToastService> logger)
{
    private const int MaxVisible = 3;

    public ObservableCollection<ToastMessage> Messages { get; } = [];

    public void Show(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        logger.LogInformation("{Toast}", text);

        while (Messages.Count >= MaxVisible)
        {
            Messages.RemoveAt(0);
        }

        Messages.Add(new ToastMessage(text));
    }
}
