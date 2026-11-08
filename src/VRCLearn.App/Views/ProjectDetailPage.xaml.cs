using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VRCLearn.App.Markup;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ProjectDetailPage : UserControl
{
    private readonly DispatcherTimer _unityTimer;

    public ProjectDetailPage()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            var viewModel = args.NewValue as ProjectDetailViewModel;
            InstalledPackageRows.Collection = viewModel?.FilteredInstalled;
            AvailablePackageRows.Collection = viewModel?.Available;
        };

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

    private void OnJumpToAvailableClick(object sender, RoutedEventArgs e) =>
        JumpToPackageSection(AvailablePackagesSection, AvailablePackageSearchBox);

    private void OnJumpToInstalledClick(object sender, RoutedEventArgs e) =>
        JumpToPackageSection(InstalledPackagesSection, InstalledPackageSearchBox);

    private void JumpToPackageSection(FrameworkElement section, TextBox searchBox)
    {
        var index = ProjectItems.Items.IndexOf(section);
        if (index < 0
            || VisualTree.FindDescendant<VirtualizingStackPanel>(ProjectItems) is not { ScrollOwner: { } viewer } panel)
        {
            return;
        }

        var start = viewer.VerticalOffset;
        panel.BringIndexIntoViewPublic(index);
        ProjectItems.UpdateLayout();
        if (SectionOffset() is not { } destination)
        {
            return;
        }

        searchBox.Focus();
        if (AnimationService.Current?.Enabled != false)
        {
            // Start near a distant section so the transition stays short even in large projects.
            var distance = viewer.ViewportHeight / 2;
            var approach = Math.Clamp(start, destination - distance, destination + distance);
            viewer.ScrollToVerticalOffset(approach);
            ProjectItems.UpdateLayout();
        }

        SmoothScroll.ScrollTo(viewer, SectionOffset);

        double? SectionOffset() => section.IsDescendantOf(panel)
            ? viewer.VerticalOffset + section.TranslatePoint(new Point(), panel).Y
            : null;
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
