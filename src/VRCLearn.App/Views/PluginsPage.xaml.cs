using System.Windows.Controls;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class PluginsPage : UserControl
{
    public PluginsPage()
    {
        InitializeComponent();

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is PluginsViewModel viewModel)
            {
                _ = viewModel.ReloadAsync();
            }
        };
    }
}
