using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App;

public partial class MainWindow : FluentWindow
{
    private const double PageShiftOffset = 6;

    private const double HeaderShiftOffset = 20;

    private const double SidebarCollapsedWidth = 64;

    private const double SidebarExpandedWidth = 220;

    private const double SidebarClipInset = 14;

    private readonly MainWindowViewModel _viewModel;
    private readonly AnimationService _animations;
    private readonly DropImportService _drops;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        MainWindowViewModel viewModel,
        AnimationService animations,
        DropImportService drops,
        ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _animations = animations;
        _drops = drops;
        _logger = logger;
        DataContext = viewModel;

        InitializeComponent();

        Sidebar.SizeChanged += (_, _) => UpdateSidebarClips();
        UpdateMaximizeButton();
        LocalizationService.Instance.PropertyChanged += (_, _) => UpdateMaximizeButton();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncNavigationSelection();

        Loaded += (_, _) =>
        {
            _shown = true;
            UpdateSidebarClips();

            if (_page is { } page)
            {
                PlayPageTransition(page);
            }
        };
    }

    private void UpdateSidebarClips()
    {
        var radius = (CornerRadius)FindResource("Radius.Control");
        var rowWidth = SidebarExpandedWidth - SidebarClipInset;
        var width = Math.Clamp(Sidebar.ActualWidth - SidebarClipInset, 0, rowWidth);

        foreach (var item in Navigation.Items.OfType<ListBoxItem>())
        {
            if (item.Template?.FindName("Row", item) is not Border row)
            {
                continue;
            }

            row.Width = rowWidth;
            row.Clip = new RectangleGeometry(
                new Rect(0, 0, width, Math.Max(0, row.ActualHeight)),
                radius.TopLeft,
                radius.TopLeft);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
        {
            ShowPage(_viewModel.CurrentPage);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentKey))
        {
            SyncNavigationSelection();
        }
    }

    private void SyncNavigationSelection()
    {
        foreach (var item in Navigation.Items.OfType<ListBoxItem>())
        {
            if (item.Tag as string == _viewModel.CurrentKey)
            {
                if (!ReferenceEquals(Navigation.SelectedItem, item))
                {
                    Navigation.SelectedItem = item;
                }

                return;
            }
        }

        if (Navigation.SelectedItem is not null)
        {
            Navigation.SelectedItem = null;
        }
    }

    private readonly Dictionary<object, FrameworkElement> _views = [];

    private FrameworkElement? _page;
    private bool _shown;

    private void ShowPage(object viewModel)
    {
        if (_page?.DataContext is IPageViewModel leaving && !ReferenceEquals(_page.DataContext, viewModel))
        {
            leaving.Release();
        }

        foreach (var view in _views.Values)
        {
            view.Visibility = Visibility.Collapsed;
        }

        var target = ViewFor(viewModel);
        target.Visibility = Visibility.Visible;

        _page = target;

        if (_shown)
        {
            target.UpdateLayout();
            PlayPageTransition(target);
        }
    }

    private FrameworkElement ViewFor(object viewModel)
    {
        if (_views.TryGetValue(viewModel, out var cached))
        {
            return cached;
        }

        var template = (DataTemplate)FindResource(new DataTemplateKey(viewModel.GetType()));
        var view = (FrameworkElement)template.LoadContent();
        view.DataContext = viewModel;
        view.Visibility = Visibility.Collapsed;
        view.RenderTransform = new TranslateTransform();

        PageHost.Children.Add(view);
        _views[viewModel] = view;

        return view;
    }

    private void PlayPageTransition(FrameworkElement page)
    {
        AnimationService.PlayFadeShift(
            page,
            (TranslateTransform)page.RenderTransform,
            0,
            PageShiftOffset,
            _animations.Enabled);

        AnimationService.PlayFadeShift(
            PageHeader,
            (TranslateTransform)PageHeader.RenderTransform,
            -HeaderShiftOffset,
            0,
            _animations.Enabled);
    }

    private void OnNavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is ListBoxItem { Tag: string key })
        {
            _viewModel.Navigate(key);
        }
    }

    private void OnSidebarMouseEnter(object sender, MouseEventArgs e) => AnimateSidebar(SidebarExpandedWidth);

    private void OnSidebarMouseLeave(object sender, MouseEventArgs e) => AnimateSidebar(SidebarCollapsedWidth);

    private void AnimateSidebar(double width)
    {
        if (!_animations.Enabled)
        {
            Sidebar.BeginAnimation(WidthProperty, null);
            Sidebar.Width = width;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = width,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        Sidebar.BeginAnimation(WidthProperty, animation);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        if (MaximizeIcon is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;

        MaximizeIcon.Symbol = maximized ? SymbolRegular.SquareMultiple24 : SymbolRegular.Square24;

        var text = LocalizationService.Instance[maximized ? "Caption.Restore" : "Caption.Maximize"];

        MaximizeButton.ToolTip = text;
        AutomationProperties.SetName(MaximizeButton, text);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_viewModel.Progress.IsRunning)
        {
            e.Cancel = true;
        }
    }

    private void OnCaptionDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || WindowState == WindowState.Maximized)
        {
            return;
        }

        DragMove();
    }

    private void OnPageDragOver(object sender, DragEventArgs e)
    {
        var valid = _viewModel.ShowDropZone
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && DropImportService.CanAccept(_viewModel.CurrentKey, paths);

        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnPageDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!_viewModel.ShowDropZone
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        try
        {
            await _drops.ImportAsync(_viewModel.CurrentKey, paths);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "拖放导入失败");
        }
    }
}
