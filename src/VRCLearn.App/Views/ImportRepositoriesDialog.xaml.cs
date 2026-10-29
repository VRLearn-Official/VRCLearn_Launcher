using System.IO;
using System.Windows;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Repositories;

namespace VRCLearn.App.Views;

public partial class ImportRepositoriesDialog : DialogWindowBase
{
    public ImportRepositoriesDialog() => InitializeComponent();

    private RepositoriesViewModel ViewModel => (RepositoriesViewModel)DataContext;

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var selected = DialogService.Instance.PickFile(
            "",
            $"{RepositoriesFile.Name}|*.txt",
            RepositoriesFile.Name,
            this);

        if (selected is not null)
        {
            ViewModel.ImportFilePath = selected;
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ImportFromFile)
        {
            if (!File.Exists(ViewModel.ImportFilePath))
            {
                ViewModel.ReportMissingFile(ViewModel.ImportFilePath);
                return;
            }

            await ViewModel.ImportAsync(await File.ReadAllTextAsync(ViewModel.ImportFilePath));
        }
        else
        {
            await ViewModel.DownloadAndImportCommand.ExecuteAsync(null);
        }

        DialogResult = true;
    }
}
