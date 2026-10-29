using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ProjectsPage : UserControl
{
    /// <summary>「⋯」菜单当前对应的那一行。</summary>
    private ProjectItemViewModel? _menuProject;

    private readonly DispatcherTimer _unityTimer;

    public ProjectsPage()
    {
        InitializeComponent();

        _unityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unityTimer.Tick += (_, _) => (DataContext as ProjectsViewModel)?.RefreshUnityStates();

        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, _) => _unityTimer.Stop();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is ProjectsViewModel viewModel)
        {
            _ = viewModel.ReloadAsync();
            _unityTimer.Start();
        }
        else
        {
            _unityTimer.Stop();
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectsViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(LocalizationService.Instance["Projects.PickerTitle"]);

        if (selected is not null)
        {
            await viewModel.AddAsync(selected);
        }
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (FromButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: ProjectItemViewModel project }
            && DataContext is ProjectsViewModel viewModel)
        {
            viewModel.OpenCommand.Execute(project);
        }
    }

    private static bool FromButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button)
        {
            return;
        }

        _menuProject = button.DataContext as ProjectItemViewModel;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => Execute(viewModel => viewModel.OpenFolderCommand);

    private void OnRenameClick(object sender, RoutedEventArgs e) => Execute(viewModel => viewModel.RenameCommand);

    private void OnRemoveClick(object sender, RoutedEventArgs e) => Execute(viewModel => viewModel.RemoveCommand);

    private void OnArchiveClick(object sender, RoutedEventArgs e) => Execute(viewModel => viewModel.ArchiveCommand);

    private void Execute(Func<ProjectsViewModel, ICommand> pick)
    {
        if (_menuProject is { } project && DataContext is ProjectsViewModel viewModel)
        {
            pick(viewModel).Execute(project);
        }
    }
}
