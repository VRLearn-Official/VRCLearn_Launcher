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
    private readonly SectionAnimator _backupsSection = new();
    private readonly SectionAnimator _unityArgumentsSection = new();
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

    private void OnBackupsChecked(object sender, RoutedEventArgs e) =>
        _backupsSection.Show(this, BackupsContent, true);

    private void OnBackupsUnchecked(object sender, RoutedEventArgs e) =>
        _backupsSection.Show(this, BackupsContent, false);

    private void OnUnityArgumentsChecked(object sender, RoutedEventArgs e) =>
        _unityArgumentsSection.Show(this, UnityArgumentsContent, true);

    private void OnUnityArgumentsUnchecked(object sender, RoutedEventArgs e) =>
        _unityArgumentsSection.Show(this, UnityArgumentsContent, false);

    private sealed class SectionAnimator
    {
        private int _generation;

        public void Show(FrameworkElement page, FrameworkElement content, bool expand)
        {
            var generation = ++_generation;

            content.BeginAnimation(FrameworkElement.MaxHeightProperty, null);

            if (!page.IsVisible || AnimationService.Current?.Enabled == false)
            {
                content.MaxHeight = double.PositiveInfinity;
                content.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;

                return;
            }

            if (expand)
            {
                content.Visibility = Visibility.Visible;
                content.MaxHeight = double.PositiveInfinity;
                content.Measure(new Size(
                    content.ActualWidth > 0 ? content.ActualWidth : page.ActualWidth,
                    double.PositiveInfinity));

                var animation = new DoubleAnimation(0, content.DesiredSize.Height, AnimationService.Switch)
                {
                    EasingFunction = AnimationService.Ease,
                };

                animation.Completed += (_, _) =>
                {
                    if (generation != _generation)
                    {
                        return;
                    }

                    content.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                    content.MaxHeight = double.PositiveInfinity;
                };

                content.MaxHeight = 0;
                content.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);

                return;
            }

            var collapse = new DoubleAnimation(content.ActualHeight, 0, AnimationService.Switch)
            {
                EasingFunction = AnimationService.Ease,
            };

            collapse.Completed += (_, _) =>
            {
                if (generation != _generation)
                {
                    return;
                }

                content.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                content.Visibility = Visibility.Collapsed;
                content.MaxHeight = double.PositiveInfinity;
            };

            content.BeginAnimation(FrameworkElement.MaxHeightProperty, collapse);
        }
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
