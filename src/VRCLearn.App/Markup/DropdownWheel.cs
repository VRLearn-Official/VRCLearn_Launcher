using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VRCLearn.App.Markup;

/// <summary>列表展开时锁住页面滚动，滚轮只作用于列表。</summary>
public static class DropdownWheel
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DropdownWheel),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly MouseWheelEventHandler WindowHandler = OnWindowPreviewWheel;

    private static ComboBox? _active;

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ComboBox combo)
        {
            return;
        }

        combo.DropDownOpened -= OnDropDownOpened;
        combo.DropDownClosed -= OnDropDownClosed;

        if (e.NewValue is not true)
        {
            return;
        }

        combo.DropDownOpened += OnDropDownOpened;
        combo.DropDownClosed += OnDropDownClosed;
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        if (_active is { } previous && Window.GetWindow(previous) is { } previousWindow)
        {
            previousWindow.PreviewMouseWheel -= WindowHandler;
        }

        _active = combo;

        if (Window.GetWindow(combo) is { } window)
        {
            window.PreviewMouseWheel -= WindowHandler;
            window.PreviewMouseWheel += WindowHandler;
        }
    }

    private static void OnDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo || !ReferenceEquals(_active, combo))
        {
            return;
        }

        _active = null;

        if (Window.GetWindow(combo) is { } window)
        {
            window.PreviewMouseWheel -= WindowHandler;
        }
    }

    private static void OnWindowPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (_active is not { IsDropDownOpen: true } combo || FindList(combo) is not { } list)
        {
            return;
        }

        e.Handled = true;

        list.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
        });
    }

    private static ScrollViewer? FindList(ComboBox combo) =>
        FindPopup(combo)?.Child is { } child ? VisualTree.FindDescendant<ScrollViewer>(child) : null;

    private static Popup? FindPopup(DependencyObject node)
    {
        if (node is Popup popup)
        {
            return popup;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (FindPopup(VisualTreeHelper.GetChild(node, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
