using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class LogsPage : UserControl
{
    private readonly DispatcherTimer _timer;

    public LogsPage()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => (DataContext as LogsViewModel)?.Reload();

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is LogsViewModel old)
            {
                old.ScrolledToEnd -= ScrollToEnd;
                old.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (args.NewValue is LogsViewModel fresh)
            {
                fresh.ScrolledToEnd += ScrollToEnd;
                fresh.PropertyChanged += OnViewModelPropertyChanged;
                fresh.Reload();
            }
        };

        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, _) => _timer.Stop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LogsViewModel.AutoRefresh) || sender is not LogsViewModel viewModel)
        {
            return;
        }

        if (IsVisible && viewModel.AutoRefresh)
        {
            _timer.Start();
            ScrollToEnd();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is not LogsViewModel viewModel)
        {
            return;
        }

        if (e.NewValue is not true)
        {
            _timer.Stop();
            return;
        }

        viewModel.Reload();

        if (viewModel.AutoRefresh)
        {
            _timer.Start();
            ScrollToEnd();
        }
    }

    private void ScrollToEnd() => LogText.ScrollToEnd();

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogsViewModel viewModel)
        {
            return;
        }

        var target = DialogService.Instance.PickSaveFile(
            LocalizationService.Instance["Logs.Export"],
            $"vrl-{DateTime.Now:yyyy-MM-dd-HHmmss}.log",
            "*.log|*.log|*.*|*.*");

        if (target is not null)
        {
            await viewModel.ExportAsync(target);
        }
    }
}
