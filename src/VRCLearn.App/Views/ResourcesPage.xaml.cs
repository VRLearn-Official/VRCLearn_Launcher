using System.Windows.Controls;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ResourcesPage : UserControl
{
    public ResourcesPage()
    {
        InitializeComponent();

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is ResourcesViewModel viewModel)
            {
                viewModel.Reload();
            }
        };
    }
}
