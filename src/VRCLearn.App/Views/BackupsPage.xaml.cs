using System.Windows.Controls;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class BackupsPage : UserControl
{
    public BackupsPage()
    {
        InitializeComponent();

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is BackupsViewModel viewModel)
            {
                viewModel.Reload();
            }
        };
    }
}
