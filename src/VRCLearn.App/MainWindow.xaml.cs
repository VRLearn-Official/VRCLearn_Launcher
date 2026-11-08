using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App;

public partial class MainWindow : FluentWindow
{
    private const double CaptionHeight = 88;

    private const double PageShiftOffset = 6;

    private const double HeaderShiftOffset = 20;

    private const int MaxCachedViews = 3;

    private readonly MainWindowViewModel _viewModel;
    private readonly AnimationService _animations;
    private readonly DropImportService _drops;
    private readonly ILogger<MainWindow> _logger;

    private string[]? _lastDropPaths;
    private bool _lastDropValid;
    private int _dragGeneration;

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

        UpdateMaximizeButton();
        LocalizationService.Instance.PropertyChanged += (_, _) => UpdateMaximizeButton();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncNavigationSelection();

        Loaded += (_, _) =>
        {
            _shown = true;

            if (_page is { } page)
            {
                PlayPageTransition(page);
            }
        };
    }

    protected override void SetWindowChrome()
    {
        base.SetWindowChrome();

        WindowChrome.SetWindowChrome(
            this,
            new WindowChrome
            {
                CaptionHeight = CaptionHeight,
                CornerRadius = default,
                GlassFrameThickness = WindowBackdropType == WindowBackdropType.None
                    ? new Thickness(0.00001)
                    : new Thickness(-1),
                ResizeBorderThickness = ResizeMode == ResizeMode.NoResize
                    ? default
                    : new Thickness(4),
                UseAeroCaptionButtons = false,
            });
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
    private readonly List<object> _viewOrder = [];

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
        TrimViews(viewModel);

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
            _viewOrder.Remove(viewModel);
            _viewOrder.Add(viewModel);

            return cached;
        }

        var template = (DataTemplate)FindResource(new DataTemplateKey(viewModel.GetType()));
        var view = (FrameworkElement)template.LoadContent();
        view.DataContext = viewModel;
        view.Visibility = Visibility.Collapsed;
        view.RenderTransform = new TranslateTransform();

        PageHost.Children.Add(view);
        _views[viewModel] = view;
        _viewOrder.Add(viewModel);

        return view;
    }

    private void TrimViews(object current)
    {
        while (_views.Count > MaxCachedViews)
        {
            object? stale = null;

            foreach (var candidate in _viewOrder)
            {
                if (!ReferenceEquals(candidate, current))
                {
                    stale = candidate;
                    break;
                }
            }

            if (stale is null || !_views.Remove(stale, out var view))
            {
                return;
            }

            _viewOrder.Remove(stale);
            view.DataContext = null;
            PageHost.Children.Remove(view);
        }
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

    private bool IsDropAllowed =>
        _viewModel.CurrentKey != "setup"
        && !_drops.IsBusy
        && !_viewModel.Progress.IsRunning;

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Handled = true;
        _dragGeneration++;

        var valid = IsDropAllowed && GetDropPaths(e) is { } paths && IsDropValid(paths);

        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        var generation = _dragGeneration;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (generation == _dragGeneration)
            {
                HideDropOverlay();
            }
        }));
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        _dragGeneration++;
        e.Handled = true;
        HideDropOverlay();

        if (!IsDropAllowed || GetDropPaths(e) is not { } paths)
        {
            return;
        }

        try
        {
            await _drops.ImportAsync(paths);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "拖放导入失败");
        }
        finally
        {
            Activate();
        }
    }

    private static string[]? GetDropPaths(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 ? paths : null;

    private bool IsDropValid(string[] paths)
    {
        if (ReferenceEquals(paths, _lastDropPaths))
        {
            return _lastDropValid;
        }

        if (_lastDropPaths is not null
            && paths.Length == _lastDropPaths.Length
            && paths.SequenceEqual(_lastDropPaths, StringComparer.OrdinalIgnoreCase))
        {
            return _lastDropValid;
        }

        _lastDropPaths = paths;
        _lastDropValid = DropImportService.CanAccept(paths);

        return _lastDropValid;
    }

    private void HideDropOverlay()
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        _lastDropPaths = null;
        _lastDropValid = false;
    }
}
