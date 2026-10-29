using System.Windows;
using System.Windows.Controls;

namespace VRCLearn.App.Views;

public sealed class DialogChrome : ContentControl
{
    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle),
        typeof(bool),
        typeof(DialogChrome),
        new PropertyMetadata(true));

    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }
}
