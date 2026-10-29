using System.Windows;
using System.Windows.Shell;
using Wpf.Ui.Controls;

namespace VRCLearn.App.Views;

public class DialogWindowBase : FluentWindow
{
    protected override void SetWindowChrome()
    {
        base.SetWindowChrome();

        WindowChrome.SetWindowChrome(
            this,
            new WindowChrome
            {
                CaptionHeight = 44,
                CornerRadius = default,
                GlassFrameThickness = new Thickness(0.00001),
                ResizeBorderThickness = ResizeMode == ResizeMode.NoResize
                    ? default
                    : new Thickness(4),
                UseAeroCaptionButtons = false,
            });
    }
}
