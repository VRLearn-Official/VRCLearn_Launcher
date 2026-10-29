using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VRCLearn.App.Services;

public sealed class AnimationService
{
    public AnimationService() => Current = this;

    public static AnimationService? Current { get; private set; }

    public bool Enabled { get; private set; } = true;

    public void Apply(bool enabled) => Enabled = enabled;

    public static Duration Switch { get; } = new(TimeSpan.FromMilliseconds(220));

    public static Duration Quick { get; } = new(TimeSpan.FromMilliseconds(140));

    public static IEasingFunction Ease { get; } = new SineEase { EasingMode = EasingMode.EaseOut };

    public static void PlayFadeShift(
        UIElement element,
        TranslateTransform shift,
        double offsetX,
        double offsetY,
        bool animate)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);

        element.Opacity = 1;
        shift.X = 0;
        shift.Y = 0;

        if (!animate)
        {
            return;
        }

        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, Switch) { EasingFunction = Ease });
        shift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(offsetX, 0, Switch) { EasingFunction = Ease });
        shift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(offsetY, 0, Switch) { EasingFunction = Ease });
    }
}
