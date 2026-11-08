using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ProjectDetailPage : UserControl
{
    private readonly DispatcherTimer _unityTimer;
    private int _backupsAnimation;

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

    private void OnBackupsChecked(object sender, RoutedEventArgs e) => ShowBackups(true);

    private void OnBackupsUnchecked(object sender, RoutedEventArgs e) => ShowBackups(false);

    private void ShowBackups(bool expand)
    {
        var generation = ++_backupsAnimation;

        BackupsContent.BeginAnimation(MaxHeightProperty, null);

        if (!IsVisible || AnimationService.Current?.Enabled == false)
        {
            BackupsContent.MaxHeight = double.PositiveInfinity;
            BackupsContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;

            return;
        }

        if (expand)
        {
            BackupsContent.Visibility = Visibility.Visible;
            BackupsContent.MaxHeight = double.PositiveInfinity;
            BackupsContent.Measure(new Size(
                BackupsContent.ActualWidth > 0 ? BackupsContent.ActualWidth : ActualWidth,
                double.PositiveInfinity));

            var animation = new DoubleAnimation(0, BackupsContent.DesiredSize.Height, AnimationService.Switch)
            {
                EasingFunction = AnimationService.Ease,
            };

            animation.Completed += (_, _) =>
            {
                if (generation != _backupsAnimation)
                {
                    return;
                }

                BackupsContent.BeginAnimation(MaxHeightProperty, null);
                BackupsContent.MaxHeight = double.PositiveInfinity;
            };

            BackupsContent.MaxHeight = 0;
            BackupsContent.BeginAnimation(MaxHeightProperty, animation);

            return;
        }

        var collapse = new DoubleAnimation(BackupsContent.ActualHeight, 0, AnimationService.Switch)
        {
            EasingFunction = AnimationService.Ease,
        };

        collapse.Completed += (_, _) =>
        {
            if (generation != _backupsAnimation)
            {
                return;
            }

            BackupsContent.BeginAnimation(MaxHeightProperty, null);
            BackupsContent.Visibility = Visibility.Collapsed;
            BackupsContent.MaxHeight = double.PositiveInfinity;
        };

        BackupsContent.BeginAnimation(MaxHeightProperty, collapse);
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
