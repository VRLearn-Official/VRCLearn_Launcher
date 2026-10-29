using System.Windows;
using System.Windows.Input;

namespace VRCLearn.App.Styles;

public partial class DialogChromeStyles : ResourceDictionary
{
    public DialogChromeStyles()
    {
        InitializeComponent();
    }

    private void OnCaptionDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && Window.GetWindow((DependencyObject)sender) is { } window)
        {
            window.DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        Window.GetWindow((DependencyObject)sender)?.Close();
}
