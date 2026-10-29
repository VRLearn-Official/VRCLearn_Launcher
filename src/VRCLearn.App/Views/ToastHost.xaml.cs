using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ToastHost : UserControl
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    private const double Offset = 48;

    public static readonly DependencyProperty ServiceProperty =
        DependencyProperty.Register(nameof(Service), typeof(ToastService), typeof(ToastHost));

    public ToastHost() => InitializeComponent();

    public ToastService? Service
    {
        get => (ToastService?)GetValue(ServiceProperty);
        set => SetValue(ServiceProperty, value);
    }

    private async void OnToastLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToastMessage message } card)
        {
            return;
        }

        var shift = new TranslateTransform();
        var animate = AnimationService.Current?.Enabled != false;

        card.RenderTransform = shift;

        if (animate)
        {
            Slide(card, shift, Offset, 0, 0, 1);
        }

        await Task.Delay(Lifetime);

        if (animate)
        {
            Slide(card, shift, 0, Offset, 1, 0);
            await Task.Delay(AnimationService.Quick.TimeSpan);
        }

        Service?.Messages.Remove(message);
    }

    private static void Slide(
        UIElement card,
        TranslateTransform shift,
        double fromX,
        double toX,
        double fromOpacity,
        double toOpacity)
    {
        card.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(fromOpacity, toOpacity, AnimationService.Quick)
            {
                EasingFunction = AnimationService.Ease,
            });

        shift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(fromX, toX, AnimationService.Quick)
            {
                EasingFunction = AnimationService.Ease,
            });
    }
}
