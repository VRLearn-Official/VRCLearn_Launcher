using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class SetupPage : UserControl
{
    private const double StepShiftOffset = 14;

    public SetupPage()
    {
        InitializeComponent();

        foreach (var panel in Steps.Children.OfType<FrameworkElement>())
        {
            panel.RenderTransform = new TranslateTransform();
            panel.IsVisibleChanged += OnPanelVisibleChanged;
        }
    }

    private void OnPanelVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not FrameworkElement panel)
        {
            return;
        }

        Scroller.ScrollToTop();

        AnimationService.PlayFadeShift(
            panel,
            (TranslateTransform)panel.RenderTransform,
            0,
            StepShiftOffset,
            AnimationService.Current?.Enabled == true);
    }

    private void OnBrowseUnityHubClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFile(
            LocalizationService.Instance["Settings.UnityHub"],
            "Unity Hub.exe|Unity Hub.exe|*.exe|*.*");

        if (selected is not null)
        {
            viewModel.Shared.UnityHubPath = selected;
        }
    }

    private void OnAddUnityEditorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFile(
            LocalizationService.Instance["Setup.UnityVersionsAdd"],
            "Unity.exe|Unity.exe|*.exe|*.*");

        if (selected is not null)
        {
            viewModel.AddUnityEditor(selected);
        }
    }

    private void OnBrowseProjectDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(
            LocalizationService.Instance["Settings.ProjectPath"],
            viewModel.Shared.ActualProjectDirectory);

        if (selected is not null)
        {
            viewModel.Shared.ProjectDirectory = selected;
        }
    }

    private void OnBrowseBackupDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(
            LocalizationService.Instance["Settings.BackupDirectory"],
            viewModel.Shared.BackupDirectory);

        if (selected is not null)
        {
            viewModel.Shared.BackupDirectory = selected;
        }
    }

    private async void OnBrowseCacheDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(
            LocalizationService.Instance["Settings.CachePath"],
            viewModel.Cache.Current);

        if (selected is not null)
        {
            await viewModel.Cache.ChangeAsync(selected);
        }
    }

    private async void OnRestoreCacheDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel viewModel)
        {
            await viewModel.Cache.RestoreAsync();
        }
    }
}
