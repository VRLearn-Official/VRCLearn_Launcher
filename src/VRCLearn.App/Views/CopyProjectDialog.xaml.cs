using System.Windows;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class CopyProjectDialog : DialogWindowBase
{
    public CopyProjectDialog()
    {
        InitializeComponent();
    }

    private CopyProjectViewModel ViewModel => (CopyProjectViewModel)DataContext;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Destination.Trim().Length == 0)
        {
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
