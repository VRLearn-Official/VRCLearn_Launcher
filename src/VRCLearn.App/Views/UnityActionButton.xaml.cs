using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VRCLearn.App.Views;

public partial class UnityActionButton : UserControl
{
    public static readonly DependencyProperty LaunchCommandProperty =
        DependencyProperty.Register(nameof(LaunchCommand), typeof(ICommand), typeof(UnityActionButton));

    public static readonly DependencyProperty BringToFrontCommandProperty =
        DependencyProperty.Register(nameof(BringToFrontCommand), typeof(ICommand), typeof(UnityActionButton));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(UnityActionButton));

    public static readonly DependencyProperty IsUnityRunningProperty =
        DependencyProperty.Register(nameof(IsUnityRunning), typeof(bool), typeof(UnityActionButton));

    public static readonly DependencyProperty ButtonStyleProperty =
        DependencyProperty.Register(nameof(ButtonStyle), typeof(Style), typeof(UnityActionButton));

    public static readonly DependencyProperty ButtonFocusableProperty =
        DependencyProperty.Register(nameof(ButtonFocusable), typeof(bool), typeof(UnityActionButton), new PropertyMetadata(true));

    public UnityActionButton() => InitializeComponent();

    public ICommand? LaunchCommand
    {
        get => (ICommand?)GetValue(LaunchCommandProperty);
        set => SetValue(LaunchCommandProperty, value);
    }

    public ICommand? BringToFrontCommand
    {
        get => (ICommand?)GetValue(BringToFrontCommandProperty);
        set => SetValue(BringToFrontCommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool IsUnityRunning
    {
        get => (bool)GetValue(IsUnityRunningProperty);
        set => SetValue(IsUnityRunningProperty, value);
    }

    public Style? ButtonStyle
    {
        get => (Style?)GetValue(ButtonStyleProperty);
        set => SetValue(ButtonStyleProperty, value);
    }

    public bool ButtonFocusable
    {
        get => (bool)GetValue(ButtonFocusableProperty);
        set => SetValue(ButtonFocusableProperty, value);
    }
}
