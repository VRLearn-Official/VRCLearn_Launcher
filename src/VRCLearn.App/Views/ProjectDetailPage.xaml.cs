using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ProjectDetailPage : UserControl
{
    private readonly DispatcherTimer _unityTimer;
    private int _backupsAnimation;
    private bool _updatingBatchLayout;

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

    private void OnBatchToolbarLoaded(object sender, RoutedEventArgs e) => UpdateBatchLayout();

    private void OnBatchToolbarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateBatchLayout();

    private void UpdateBatchLayout()
    {
        if (_updatingBatchLayout || BatchToolbar?.IsLoaded != true)
        {
            return;
        }

        _updatingBatchLayout = true;
        try
        {
            // Keep the whole strip on one line. Less frequent actions remain available in the menu.
            FrameworkElement[] priority =
            [
                ReinstallSelectedButton, RemoveSelectedButton, ClearSelectionButton,
                UpgradeSelectedButton, InstallSelectedButton, UpgradeAllButton,
            ];
            var size = new Size(double.PositiveInfinity, double.PositiveInfinity);
            BatchTitle.Measure(size);
            BatchMoreButton.Visibility = Visibility.Visible;
            BatchMoreButton.Measure(size);
            var available = Math.Max(0, BatchToolbar.ActualWidth - BatchTitle.DesiredSize.Width);
            var total = 0.0;
            foreach (var button in priority)
            {
                button.Visibility = Visibility.Visible;
                button.InvalidateMeasure();
                button.Measure(size);
                total += button.DesiredSize.Width;
            }

            if (total <= available + 0.5)
            {
                BatchMoreButton.Visibility = Visibility.Collapsed;
                return;
            }

            total += BatchMoreButton.DesiredSize.Width;
            foreach (var button in priority)
            {
                if (total <= available + 0.5)
                {
                    break;
                }

                total -= button.DesiredSize.Width;
                button.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            _updatingBatchLayout = false;
        }
    }

    private void OnBatchMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OnBatchMenuOpened(object sender, RoutedEventArgs e)
    {
        (FrameworkElement Button, MenuItem Item)[] overflow =
        [
            (InstallSelectedButton, InstallSelectedOverflowItem),
            (UpgradeSelectedButton, UpgradeSelectedOverflowItem),
            (UpgradeAllButton, UpgradeAllOverflowItem),
            (ReinstallSelectedButton, ReinstallSelectedOverflowItem),
            (RemoveSelectedButton, RemoveSelectedOverflowItem),
            (ClearSelectionButton, ClearSelectionOverflowItem),
        ];
        foreach (var (button, item) in overflow)
        {
            item.Visibility = button.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        }

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

    private void OnUpgradeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackageRowViewModel { NewerVersion: not null } package }
            && DataContext is ProjectDetailViewModel viewModel)
        {
            _ = viewModel.UpgradeToVersionAsync(package);
        }
    }

}
