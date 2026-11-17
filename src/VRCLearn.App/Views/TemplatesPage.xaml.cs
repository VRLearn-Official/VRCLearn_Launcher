using System.Windows.Controls;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class TemplatesPage : UserControl
{
    public TemplatesPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is TemplatesViewModel viewModel)
            {
                _ = viewModel.ReloadAsync();
            }
        };
    }
}
