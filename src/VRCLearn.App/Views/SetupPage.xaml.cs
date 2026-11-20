using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VRCLearn.App.Services;

namespace VRCLearn.App.Views;

public partial class SetupPage : UserControl
{
    private const double StepShiftOffset = 14;

    public SetupPage()
    {
        InitializeComponent();

        foreach (var panel in Steps.Children.OfType<FrameworkElement>())
        {
            panel.RenderTransform = new TranslateTransform();
            panel.IsVisibleChanged += OnPanelVisibleChanged;
        }
    }

    private void OnPanelVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not FrameworkElement panel)
        {
            return;
        }

        Scroller.ScrollToTop();

        AnimationService.PlayFadeShift(
            panel,
            (TranslateTransform)panel.RenderTransform,
            0,
            StepShiftOffset,
            AnimationService.Current?.Enabled == true);
    }

}
