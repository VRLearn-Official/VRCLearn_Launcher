using System.IO;
using System.Windows;
using System.Windows.Controls;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Repositories;

namespace VRCLearn.App.Views;

public partial class RepositoriesPage : UserControl
{
    public RepositoriesPage()
    {
        InitializeComponent();

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is RepositoriesViewModel viewModel)
            {
                viewModel.Reload();
            }
        };
    }

    private async void OnAddUserPackageClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepositoriesViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(LocalizationService.Instance["Packages.User.Add"]);

        if (selected is not null)
        {
            await viewModel.UserPackages.AddAsync(selected);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepositoriesViewModel viewModel)
        {
            return;
        }

        var target = DialogService.Instance.PickSaveFile(
            RepositoriesFile.Name,
            RepositoriesFile.Name,
            $"{RepositoriesFile.Name}|*.txt|*|*");

        if (target is not null)
        {
            File.WriteAllText(target, viewModel.Export());
        }
    }
}
