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
}
