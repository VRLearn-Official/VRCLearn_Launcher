using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ProjectDetailPage : UserControl
{
    private readonly DispatcherTimer _unityTimer;

    public ProjectDetailPage()
    {
        InitializeComponent();

        _unityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unityTimer.Tick += (_, _) => (DataContext as ProjectDetailViewModel)?.RefreshUnityState();

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is ProjectDetailViewModel viewModel)
            {
                _ = viewModel.LoadAsync();
                _unityTimer.Start();
            }
            else
            {
                _unityTimer.Stop();
            }
        };

        Unloaded += (_, _) => _unityTimer.Stop();
    }

    private void OnUpgradeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InstalledPackageViewModel { NewerVersion: { } version } package }
            && DataContext is ProjectDetailViewModel viewModel)
        {
            _ = viewModel.SwitchToVersionAsync(package.Name, version);
        }
    }

    private void OnBrowseCopyDestinationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectDetailViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(
            LocalizationService.Instance["ProjectDetail.Copy.Title"],
            Path.GetDirectoryName(viewModel.CopyDestination ?? "") ?? "");

        if (selected is not null)
        {
            viewModel.CopyDestination = Path.Combine(
                selected,
                Path.GetFileName(viewModel.CopyDestination ?? "") is { Length: > 0 } name
                    ? name
                    : viewModel.ProjectName);
        }
    }
}
