using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Vpm;
using VRCLearn.Core.Repositories;

namespace VRCLearn.App.Views;

public partial class RepositoriesPage : UserControl
{
    /// <summary>空档缩着时的高度。</summary>
    private const double ClosedSlot = 2;

    private const double OpenSlotOpacity = 0.18;

    private RepositoryItemViewModel? _dragged;
    private Point _dragOrigin;
    private bool _isDragging;

    /// <summary>当前滑开的空档，同时只有一个。</summary>
    private Border? _slot;

    /// <summary>这个空档代表插到第几位。</summary>
    private int _slotIndex = -1;

    public RepositoriesPage()
    {
        InitializeComponent();

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is RepositoriesViewModel viewModel)
            {
                viewModel.Reload();
            }
        };
    }

    private async void OnAddUserPackageClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepositoriesViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(LocalizationService.Instance["Packages.User.Add"]);

        if (selected is not null)
        {
            await viewModel.UserPackages.AddAsync(selected);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepositoriesViewModel viewModel)
        {
            return;
        }

        var target = DialogService.Instance.PickSaveFile(
            RepositoriesFile.Name,
            RepositoriesFile.Name,
            $"{RepositoriesFile.Name}|*.txt|*|*");

        if (target is not null)
        {
            File.WriteAllText(target, viewModel.Export());
        }
    }

    // ---- 拖动排序 ----

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RepositoryItemViewModel item })
        {
            return;
        }

        _dragged = item;
        _dragOrigin = e.GetPosition(this);
        e.Handled = true;
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragged is not { } dragged
            || _isDragging
            || e.LeftButton != MouseButtonState.Pressed
            || sender is not UIElement row)
        {
            return;
        }

        var offset = e.GetPosition(this) - _dragOrigin;

        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        dragged.IsDragging = true;

        try
        {
            DragDrop.DoDragDrop(row, dragged, DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
            dragged.IsDragging = false;
            _dragged = null;
            CloseSlot();
        }
    }

    private void OnRowMouseUp(object sender, MouseButtonEventArgs e) => _dragged = null;

    private void OnPageDragOver(object sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Effects = _dragged is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (_dragged is not { } dragged
            || sender is not FrameworkElement { DataContext: RepositoryItemViewModel target } row)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var below = IsBelow(row, e);
        var insertBefore = target.Index + (below ? 1 : 0);

        if (RepositoryMove.Target(dragged.Index, insertBefore) == dragged.Index)
        {
            CloseSlot();
        }
        else
        {
            SetSlot(row, below, insertBefore);
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        if (_dragged is not { } dragged
            || sender is not FrameworkElement { DataContext: RepositoryItemViewModel target } row
            || DataContext is not RepositoriesViewModel viewModel)
        {
            return;
        }

        var index = target.Index + (IsBelow(row, e) ? 1 : 0);

        e.Handled = true;
        CloseSlot();
        _dragged = null;

        viewModel.Move(dragged, index);
    }

    private static FrameworkElement? ContentOf(FrameworkElement row) =>
        row.FindName("RowContent") as FrameworkElement;

    private static bool IsBelow(FrameworkElement row, DragEventArgs e) =>
        ContentOf(row) is { } content && e.GetPosition(content).Y > content.ActualHeight / 2;

    private void SetSlot(FrameworkElement row, bool below, int index)
    {
        if (_slot is not null && _slotIndex == index)
        {
            return;
        }

        CloseSlot();

        var name = below ? "SlotBelow" : "SlotAbove";

        if (ContentOf(row) is not { } content || row.FindName(name) is not Border slot)
        {
            return;
        }

        _slot = slot;
        _slotIndex = index;

        var open = content.ActualHeight;

        slot.BeginAnimation(HeightProperty, null);
        slot.BeginAnimation(OpacityProperty, null);

        if (AnimationService.Current?.Enabled != true)
        {
            slot.Height = open;
            slot.Opacity = OpenSlotOpacity;
            return;
        }

        slot.BeginAnimation(HeightProperty, SlotAnimation(open));
        slot.BeginAnimation(OpacityProperty, SlotAnimation(OpenSlotOpacity));
    }

    private void CloseSlot()
    {
        if (_slot is not { } slot)
        {
            return;
        }

        _slot = null;
        _slotIndex = -1;

        if (AnimationService.Current?.Enabled != true)
        {
            slot.BeginAnimation(HeightProperty, null);
            slot.BeginAnimation(OpacityProperty, null);
            slot.Height = ClosedSlot;
            slot.Opacity = 0;
            return;
        }

        slot.BeginAnimation(HeightProperty, SlotAnimation(ClosedSlot, FillBehavior.Stop));
        slot.BeginAnimation(OpacityProperty, SlotAnimation(0, FillBehavior.Stop));
    }

    private static DoubleAnimation SlotAnimation(double to, FillBehavior fill = FillBehavior.HoldEnd) =>
        new(to, AnimationService.Quick) { EasingFunction = AnimationService.Ease, FillBehavior = fill };
}
