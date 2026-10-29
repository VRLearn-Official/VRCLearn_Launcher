using System.Windows;

namespace VRCLearn.App.Styles;

public partial class DialogChromeStyles : ResourceDictionary
{
    public DialogChromeStyles()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        Window.GetWindow((DependencyObject)sender)?.Close();
}
