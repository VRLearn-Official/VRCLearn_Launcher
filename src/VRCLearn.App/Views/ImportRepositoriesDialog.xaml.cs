using System.IO;
using System.Windows;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ImportRepositoriesDialog : DialogWindowBase
{
    public ImportRepositoriesDialog() => InitializeComponent();

    private RepositoriesViewModel ViewModel => (RepositoriesViewModel)DataContext;

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
