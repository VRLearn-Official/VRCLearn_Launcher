using System.Windows.Controls;
using System.Windows.Data;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class RepositoriesPage : UserControl
{
    public RepositoriesPage()
    {
        InitializeComponent();

        BindingOperations.SetBinding(RepositoryRows, CollectionContainer.CollectionProperty,
            new Binding("DataContext.Repositories") { Source = this });
        BindingOperations.SetBinding(UserPackageRows, CollectionContainer.CollectionProperty,
            new Binding("DataContext.UserPackages.Folders") { Source = this });

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is RepositoriesViewModel viewModel)
            {
                viewModel.Reload();
            }
        };
    }
}
