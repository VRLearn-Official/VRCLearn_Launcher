using System.Windows;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class NewProjectDialog : DialogWindowBase
{
    public NewProjectDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private NewProjectViewModel ViewModel => (NewProjectViewModel)DataContext;

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.CreateAsync())
        {
            DialogResult = true;
        }
    }
}
